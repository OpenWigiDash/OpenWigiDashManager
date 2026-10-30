# OpenWigiDashManager - atualiza o serviço de desenvolvimento SEM admin
# Pré-requisito: scripts\Setup-DevService.ps1 rodado uma vez como admin.
# Publica o serviço, para o que está rodando, troca os arquivos e inicia de novo.

param(
    [string]$Configuration = 'Debug',
    [string]$ServiceDir = 'C:\OpenWigiDash\dev-service',
    [switch]$AllowRemoteSessions = $false,
    [switch]$StopOfficial = $false,
    [switch]$SkipBuild = $false
)

$ErrorActionPreference = 'Stop'
$serviceName = 'OpenWigiDash'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$staging = Join-Path $root '.diagnostics\service-publish'

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $service) { throw 'Serviço de dev não instalado. Rode scripts\Setup-DevService.ps1 uma vez como admin.' }
if (-not (Test-Path -LiteralPath $ServiceDir)) { throw "Pasta do serviço não encontrada: $ServiceDir" }
# Com o instalador, o serviço de mesmo nome roda de Program Files: trocar os arquivos aqui não mudaria nada.
$image = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName").ImagePath.Trim('"')
if (-not $image.StartsWith($ServiceDir, [StringComparison]::OrdinalIgnoreCase)) {
    throw "O serviço $serviceName roda de $image, não do serviço de dev. Se é o instalado, desinstale-o e rode scripts\Setup-DevService.ps1 (docs\instalador.md)."
}

Write-Host "=== OpenWigiDash - deploy do serviço de dev ===" -ForegroundColor Cyan

# Passo 1: publicar fora da pasta do serviço, para não parar nada se a build falhar
Write-Host "[1/4] Publicando ($Configuration, self-contained)..." -ForegroundColor Green
if (-not $SkipBuild) {
    dotnet publish (Join-Path $root 'src\OpenWigi.Service') -c $Configuration -r win-x64 --self-contained -o $staging
    if ($LASTEXITCODE -ne 0) { throw 'Publicação falhou; o serviço atual continua como estava.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $staging 'OpenWigiService.exe'))) { throw "Publicação não encontrada: $staging" }

# Passo 2: só um processo abre o WinUSB por vez
Write-Host "[2/4] Liberando o aparelho..." -ForegroundColor Green
$official = Get-Process -Name 'WigiDashManager' -ErrorAction SilentlyContinue
if ($official) {
    if (-not $StopOfficial) { throw 'O manager oficial está com o aparelho. Use -StopOfficial para fechá-lo.' }
    $official | Stop-Process -Force
}
Get-Process -Name 'OpenWigiService' -ErrorAction SilentlyContinue |
    Where-Object { $_.SessionId -ne 0 } | Stop-Process -Force   # instância de console antiga, se houver
if ($service.Status -ne 'Stopped') {
    Stop-Service $serviceName -Force
    (Get-Service $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
}

# Passo 3: trocar os arquivos, mantendo os ajustes locais da máquina
Write-Host "[3/4] Copiando para $ServiceDir..." -ForegroundColor Green
Copy-Item -Path (Join-Path $staging '*') -Destination $ServiceDir -Recurse -Force
$local = Join-Path $ServiceDir 'appsettings.local.json'
@{ OpenWigi = @{ AllowRemoteSessions = [bool]$AllowRemoteSessions } } | ConvertTo-Json -Depth 3 | Set-Content $local -Encoding utf8

# Passo 4: iniciar e mostrar o começo do log
Write-Host "[4/4] Iniciando..." -ForegroundColor Green
$started = Get-Date
Start-Service $serviceName
(Get-Service $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(15))
Start-Sleep -Seconds 3
Get-Service $serviceName | Format-Table Name, Status
Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'OpenWigiDash'; StartTime = $started } -ErrorAction SilentlyContinue |
    Sort-Object TimeCreated | Select-Object -Last 10 | ForEach-Object { "{0:HH:mm:ss} {1}" -f $_.TimeCreated, $_.Message }

Write-Host ""
Write-Host "Serviço atualizado (AllowRemoteSessions = $([bool]$AllowRemoteSessions))." -ForegroundColor Green
