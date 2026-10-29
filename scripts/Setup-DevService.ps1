# OpenWigiDashManager - setup ÚNICO do serviço de desenvolvimento (precisa de admin uma vez)
#
# Depois dele, scripts\Deploy-DevService.ps1 atualiza e reinicia o serviço sem admin.
#
# O que faz:
#   1. Cria a pasta do serviço de dev, com escrita para o seu usuário.
#   2. Registra o serviço OpenWigiDash rodando como LocalService (conta sem privilégios).
#      Como a pasta é gravável pelo seu usuário, o serviço NÃO roda como LocalSystem:
#      quem troca o executável não ganha poderes de sistema.
#   3. Dá ao seu usuário permissão de iniciar, parar e consultar esse serviço.
#   4. Registra a origem "OpenWigiDash" no Log de Eventos (Aplicativo).
#
# Para desfazer: rode de novo com -Remove (também como admin).

param(
    [string]$ServiceDir = 'C:\OpenWigiDash\dev-service',
    [string]$User = "$env:USERDOMAIN\$env:USERNAME",
    # Se o LocalService não conseguir abrir o aparelho, reinstale com 'LocalSystem' (menos seguro:
    # aí a pasta deve ficar só com leitura para o usuário, e o deploy passa a precisar de admin).
    [ValidateSet('LocalService', 'LocalSystem')]
    [string]$Account = 'LocalService',
    [switch]$Remove = $false
)

$ErrorActionPreference = 'Stop'
$serviceName = 'OpenWigiDash'
$eventSource = 'OpenWigiDash'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Rode este script uma vez num PowerShell como administrador.' }

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Removendo o serviço $serviceName existente..." -ForegroundColor Yellow
    if ($existing.Status -ne 'Stopped') { Stop-Service $serviceName -Force }
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}
if ($Remove) {
    Write-Host "Serviço removido. A pasta $ServiceDir foi mantida." -ForegroundColor Green
    return
}

# 1. Pasta do serviço, gravável pelo usuário de dev e legível pelo LocalService
Write-Host "[1/4] Pasta $ServiceDir" -ForegroundColor Green
New-Item -ItemType Directory -Path $ServiceDir -Force | Out-Null
$acl = Get-Acl $ServiceDir
$inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
$none = [Security.AccessControl.PropagationFlags]::None
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($User, 'Modify', $inherit, $none, 'Allow')))
# Pelo SID (S-1-5-19): o nome da conta muda com o idioma do Windows ("AUTORIDADE NT\SERVIÇO LOCAL").
$localService = New-Object Security.Principal.SecurityIdentifier('S-1-5-19')
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($localService, 'ReadAndExecute', $inherit, $none, 'Allow')))
Set-Acl $ServiceDir $acl

# Um executável provisório: o serviço só precisa existir; o deploy de dev põe o real.
$exe = Join-Path $ServiceDir 'OpenWigiService.exe'

# 2. Registro como LocalService, início manual (o deploy de dev é quem inicia)
Write-Host "[2/4] Registrando o serviço como LocalService" -ForegroundColor Green
$obj = if ($Account -eq 'LocalSystem') { 'LocalSystem' } else { 'NT AUTHORITY\LocalService' }
sc.exe create $serviceName binPath= "`"$exe`"" obj= $obj start= demand DisplayName= 'OpenWigiDash (dev)' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc create falhou ($LASTEXITCODE)." }
sc.exe description $serviceName 'Dono do USB do WigiDash; entrega a tela à sessão ativa. Instalação de desenvolvimento.' | Out-Null
sc.exe failure $serviceName reset= 60 actions= restart/2000/restart/5000/restart/10000 | Out-Null

# 3. Permissão do usuário sobre o serviço: consultar, iniciar, parar e ler a configuração
Write-Host "[3/4] Permissão de iniciar/parar para $User" -ForegroundColor Green
$sid = (New-Object Security.Principal.NTAccount($User)).Translate([Security.Principal.SecurityIdentifier]).Value
$sddl = (sc.exe sdshow $serviceName | Where-Object { $_ -match '^D:' }).Trim()
$ace = "(A;;CCLCSWRPWPDTLOCRRC;;;$sid)"
if ($sddl -notlike "*$sid*") {
    # A ACE entra antes da parte de auditoria (S:), se houver.
    $new = if ($sddl -match 'S:') { $sddl -replace 'S:', "${ace}S:" } else { "$sddl$ace" }
    sc.exe sdset $serviceName $new | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc sdset falhou ($LASTEXITCODE)." }
}

# 4. Origem do Log de Eventos (criar exige admin; escrever, não)
Write-Host "[4/4] Origem '$eventSource' no Log de Eventos" -ForegroundColor Green
if (-not [Diagnostics.EventLog]::SourceExists($eventSource)) {
    [Diagnostics.EventLog]::CreateEventSource($eventSource, 'Application')
}

Write-Host ""
Write-Host "Pronto. A partir de agora, sem admin:" -ForegroundColor Cyan
Write-Host "  scripts\Deploy-DevService.ps1            publica, troca os arquivos e reinicia" -ForegroundColor White
Write-Host "  scripts\Deploy-DevService.ps1 -AllowRemoteSessions   idem, liberando a tela para o RDP" -ForegroundColor White
