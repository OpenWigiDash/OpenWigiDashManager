# OpenWigiDashManager - testa o instalador num Windows limpo, no Windows Sandbox, sem mexer neste PC.
#
#   scripts\Test-InstallerSandbox.ps1              instala, liga o WigiDash virtual e abre o manager com a prévia
#   scripts\Test-InstallerSandbox.ps1 -Uninstall   instala, confere, desinstala e confere de novo, sem abrir nada
#   scripts\Test-InstallerSandbox.ps1 -Build       gera o instalador antes (scripts\Build-Installer.ps1)
#
# O Sandbox não repassa USB: por isso o serviço instalado lá roda com o WigiDash virtual. O resultado
# fica em .artifacts\sandbox\logs\resultado.txt (e os logs do instalador ao lado), aqui no PC.
# Precisa do recurso "Área Restrita do Windows" (Windows Pro/Enterprise), ligado em
# "Ativar ou desativar recursos do Windows". Ver docs\instalador.md.

param(
    [string]$Installer,
    [switch]$Uninstall = $false,
    [switch]$Build = $false,
    [string]$Version = '0.0.1'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $Installer) { $Installer = Join-Path $root '.artifacts\installer\OpenWigiDash-Setup.exe' }

$sandbox = Join-Path $env:windir 'System32\WindowsSandbox.exe'
if (-not (Test-Path $sandbox)) {
    throw 'Windows Sandbox não está ligado. Ative "Área Restrita do Windows" em "Ativar ou desativar recursos do Windows" e reinicie.'
}
# Só a janela conta: os processos de serviço do Sandbox (WindowsSandboxServer, RemoteSession...) ficam
# vivos um tempo depois de fechar e não impedem abrir outro.
if (Get-Process -Name 'WindowsSandbox', 'WindowsSandboxClient' -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0) {
    throw 'Já há um Windows Sandbox aberto (só um por vez). Feche-o antes.'
}

if ($Build) {
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') -Version $Version
}
if (-not (Test-Path $Installer)) { throw "Instalador não encontrado: $Installer. Rode com -Build ou baixe o da Release." }

# Duas pastas compartilhadas: o instalador só leitura, e os logs graváveis para o resultado voltar.
$work = Join-Path $root '.artifacts\sandbox'
$inbox = Join-Path $work 'in'
$logs = Join-Path $work 'logs'
foreach ($dir in $inbox, $logs) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory $dir -Force | Out-Null
}
Copy-Item $Installer (Join-Path $inbox 'OpenWigiDash-Setup.exe')
Copy-Item (Join-Path $PSScriptRoot 'sandbox\Install-InSandbox.ps1') $inbox

$mode = if ($Uninstall) { ' -Uninstall' } else { '' }
$wsb = Join-Path $work 'OpenWigiDash.wsb'
Set-Content $wsb -Encoding utf8 -Value @"
<Configuration>
  <Networking>Enable</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$inbox</HostFolder>
      <SandboxFolder>C:\OpenWigiDashTest</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$logs</HostFolder>
      <SandboxFolder>C:\OpenWigiDashTest-logs</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\OpenWigiDashTest\Install-InSandbox.ps1 -Logs C:\OpenWigiDashTest-logs$mode</Command>
  </LogonCommand>
</Configuration>
"@

Write-Host "Abrindo o Windows Sandbox. O resultado sai em $logs\resultado.txt (leva uns 2 minutos)." -ForegroundColor Cyan
Start-Process $wsb
