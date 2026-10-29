# OpenWigiDashManager - instala um widget (projeto de plugin) para o agente do usuário atual
# Mesma ideia do deploy.ps1 do StreamDeckMirror: build, backup do anterior, cópia.
# Padrão: o widget de exemplo (relógio + contador).

param(
    [string]$Project = (Join-Path $PSScriptRoot '..\samples\OpenWigi.Samples.Clock\OpenWigi.Samples.Clock.csproj'),
    [string]$Configuration = 'Debug',
    [switch]$SkipBuild = $false
)

$ErrorActionPreference = 'Stop'
$project = Resolve-Path $Project
$name = [IO.Path]::GetFileNameWithoutExtension($project)
$widgets = Join-Path $env:LOCALAPPDATA 'OpenWigiDash\Widgets'
$target = Join-Path $widgets $name
$staging = Join-Path $env:TEMP "openwigi-widget-$name"

Write-Host "=== Deploy do widget $name ===" -ForegroundColor Cyan

Write-Host "[1/3] Build ($Configuration)..." -ForegroundColor Green
if (-not $SkipBuild) {
    dotnet publish $project -c $Configuration -o $staging
    if ($LASTEXITCODE -ne 0) { throw 'Build falhou; nada foi trocado.' }
}
if (-not (Test-Path (Join-Path $staging "$name.dll"))) { throw "Saída não encontrada: $staging" }

# O agente segura a DLL carregada: precisa estar fechado para trocar.
if (Get-Process -Name 'OpenWigiAgent' -ErrorAction SilentlyContinue) {
    throw 'Feche o OpenWigiAgent antes de trocar o widget.'
}

Write-Host "[2/3] Backup do anterior..." -ForegroundColor Green
if (Test-Path $target) {
    $backup = Join-Path $env:LOCALAPPDATA ("OpenWigiDash\Backups\$name\" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item -Path (Join-Path $target '*') -Destination $backup -Recurse -Force
    Remove-Item $target -Recurse -Force
}

Write-Host "[3/3] Copiando para $target..." -ForegroundColor Green
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $staging '*') -Destination $target -Recurse -Force

Write-Host "Widget instalado. Rode o agente: dotnet run --project src\OpenWigi.Agent -- --preview" -ForegroundColor Cyan
