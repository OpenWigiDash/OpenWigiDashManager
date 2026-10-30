# OpenWigiDashManager - publica o manager numa pasta que todos os usuários da máquina leem, SEM admin
# Serve para testar o multiusuário sem instalar nada no outro usuário: ele só abre o atalho.
# O serviço continua sendo o de dev (scripts\Deploy-DevService.ps1). Os widgets vão junto, numa pasta
# compartilhada, e o manager de teste usa ela (--plugins); layout e configurações seguem por usuário.

param(
    [string]$Configuration = 'Debug',
    [string]$TargetDir = 'C:\OpenWigiDash\dev-manager',
    [string]$WidgetsDir = 'C:\OpenWigiDash\dev-widgets'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\OpenWigi.Manager\OpenWigi.Manager.csproj'

Write-Host '=== OpenWigiDash - manager de teste para todos os usuários ===' -ForegroundColor Cyan

Write-Host "[1/3] Publicando ($Configuration, self-contained)..."
$publish = Join-Path $env:TEMP 'openwigi-dev-manager'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c $Configuration -r win-x64 --self-contained -o $publish -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Publicação falhou.' }

Write-Host "[2/3] Copiando para $TargetDir (o manager de teste aberto em qualquer sessão precisa estar fechado)..."
New-Item -ItemType Directory $TargetDir -Force | Out-Null
robocopy $publish $TargetDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Cópia falhou (robocopy $LASTEXITCODE). O manager de teste está aberto em outra sessão?" }

Write-Host "[3/3] Widgets do usuário atual para $WidgetsDir..."
$mine = Join-Path $env:LOCALAPPDATA 'OpenWigiDash\Widgets'
New-Item -ItemType Directory $WidgetsDir -Force | Out-Null
robocopy $mine $WidgetsDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Cópia dos widgets falhou (robocopy $LASTEXITCODE)." }

$launcher = Join-Path (Split-Path $TargetDir) 'Abrir OpenWigiDash (teste).cmd'
Set-Content $launcher -Encoding ascii -Value "@start """" ""$TargetDir\OpenWigiDashManager.exe"" --plugins ""$WidgetsDir"" --open /"
Write-Host "Pronto. No outro usuário: abra $launcher" -ForegroundColor Green
exit 0
