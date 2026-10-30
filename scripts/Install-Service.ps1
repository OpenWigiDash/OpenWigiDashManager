# OpenWigiDashManager - instala (ou remove) o serviço para testes, até existir o instalador.
# Precisa de PowerShell como administrador.

param(
    [string]$Configuration = "Release",
    [switch]$Uninstall = $false,
    [switch]$StopOfficial = $false
)

$ErrorActionPreference = 'Stop'
$serviceName = 'OpenWigiDash'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$publishDir = Join-Path $env:ProgramFiles 'OpenWigiDash\Service'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Rode este script num PowerShell como administrador.' }

# O instalador (MSI) registra o mesmo serviço em Program Files. Não mexer nele por aqui: desinstale o
# OpenWigiDash em Configurações > Aplicativos antes de voltar ao serviço de dev (docs\instalador.md).
$installedKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
if (Get-ItemProperty $installedKey -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq 'OpenWigiDash' -and $_.WindowsInstaller -eq 1 }) {
    throw 'O OpenWigiDash está instalado pelo instalador. Desinstale-o (Configurações > Aplicativos) antes de usar o serviço de dev.'
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Parando e removendo o serviço atual..." -ForegroundColor Yellow
    if ($existing.Status -ne 'Stopped') { Stop-Service $serviceName -Force }
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}
if ($Uninstall) {
    Write-Host "Serviço removido." -ForegroundColor Green
    return
}

# O manager oficial segura o USB; o serviço não consegue abrir o aparelho com ele rodando.
$official = Get-Process -Name 'WigiDashManager' -ErrorAction SilentlyContinue
if ($official) {
    if (-not $StopOfficial) { throw 'O manager oficial está aberto. Feche-o ou use -StopOfficial.' }
    $official | Stop-Process -Force
}

Write-Host "[1/3] Publicando o serviço ($Configuration, self-contained)..." -ForegroundColor Green
dotnet publish (Join-Path $root 'src\OpenWigi.Service') -c $Configuration -r win-x64 --self-contained -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'Publicação falhou.' }

Write-Host "[2/3] Registrando o serviço..." -ForegroundColor Green
New-Service -Name $serviceName -DisplayName 'OpenWigiDash' -Description 'Dono do USB do WigiDash; entrega a tela à sessão ativa.' `
    -BinaryPathName (Join-Path $publishDir 'OpenWigiService.exe') -StartupType Automatic | Out-Null
sc.exe failure $serviceName reset= 60 actions= restart/2000/restart/5000/restart/10000 | Out-Null

Write-Host "[3/3] Iniciando..." -ForegroundColor Green
Start-Service $serviceName
Get-Service $serviceName | Format-Table Name, Status, StartType

Write-Host "Agora rode o agente em cada usuário: src\OpenWigi.Agent (dotnet run) ou o OpenWigiAgent.exe publicado." -ForegroundColor Cyan
