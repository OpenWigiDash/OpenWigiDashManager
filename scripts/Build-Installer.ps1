# OpenWigiDashManager - gera o instalador (MSI + OpenWigiDash-Setup.exe), igual ao que a release faz.
# Não precisa de admin. Saída em .artifacts\installer\.
#
#   scripts\Build-Installer.ps1                  versão 0.0.1
#   scripts\Build-Installer.ps1 -Version 0.2.0
#   scripts\Build-Installer.ps1 -Version 0.2.0 -MsiPath x.msi   só o Setup.exe, em volta de um MSI pronto
#                                                                (a release usa com o MSI já assinado)
#
# Instalar no PC de desenvolvimento: o serviço de dev tem o mesmo nome; remova antes
# (scripts\Setup-DevService.ps1 -Remove, como admin). Ver docs\instalador.md.

param(
    [string]$Version = '0.0.1',
    [string]$Configuration = 'Release',
    [string]$MsiPath
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$out = Join-Path $root '.artifacts\installer'
$publish = Join-Path $out 'publish'

Write-Host "=== OpenWigiDash - instalador $Version ===" -ForegroundColor Cyan

if ($MsiPath) {
    $MsiPath = (Resolve-Path $MsiPath).Path
    $webView2 = Join-Path $out 'MicrosoftEdgeWebview2Setup.exe'
    if (-not (Test-Path $webView2)) { throw "Falta $webView2; rode o script sem -MsiPath antes." }
    Write-Host "Só o bundle, com $MsiPath..." -ForegroundColor Green
    dotnet build (Join-Path $root 'installer\Bundle\Bundle.wixproj') -c Release -nologo -v q `
        "-p:Version=$Version" "-p:WebView2Bootstrapper=$webView2" "-p:MsiPath=$MsiPath" -o $out
    if ($LASTEXITCODE -ne 0) { throw 'Build do WiX falhou.' }
    Get-Item (Join-Path $out 'OpenWigiDash-Setup.exe') | ForEach-Object { '{0}  {1:N1} MB' -f $_.FullName, ($_.Length / 1MB) }
    return
}

# O updater é Native AOT: precisa do compilador C++ (MSVC). Sem ele o dotnet publish falha com
# "Platform linker not found" depois de publicar o resto; melhor avisar antes.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msvc = if (Test-Path $vswhere) {
    & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}
if (-not $msvc) {
    throw @'
Falta o compilador C++ da Microsoft, que o updater (Native AOT) precisa. Instale uma vez e abra um terminal novo:
  winget install Microsoft.VisualStudio.2022.BuildTools --override "--passive --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
Ver docs\instalador.md.
'@
}

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

function Publish([string]$project, [string]$target, [switch]$Plugin) {
    $common = @('-c', $Configuration, '-o', $target, "-p:Version=$Version", '-nologo', '-v', 'q')
    # Os widgets carregam dentro do manager: não levam runtime próprio.
    if (-not $Plugin) { $common += @('-r', 'win-x64', '--self-contained') }
    dotnet publish (Join-Path $root $project) @common
    if ($LASTEXITCODE -ne 0) { throw "Publicação de $project falhou." }
}

Write-Host '[1/3] Publicando serviço, manager, updater e widgets (self-contained, win-x64)...' -ForegroundColor Green
Publish 'src\OpenWigi.Service\OpenWigi.Service.csproj' (Join-Path $publish 'service')
Publish 'src\OpenWigi.Manager\OpenWigi.Manager.csproj' (Join-Path $publish 'manager')
Publish 'samples\OpenWigi.Samples.Clock\OpenWigi.Samples.Clock.csproj' (Join-Path $publish 'widgets\OpenWigi.Samples.Clock') -Plugin
# O updater é nativo (AOT, um exe só) e vai na pasta do manager, que o chama para instalar versão nova.
Publish 'src\OpenWigi.Updater\OpenWigi.Updater.csproj' (Join-Path $publish 'updater')
Copy-Item (Join-Path $publish 'updater\OpenWigiDash.Updater.exe') (Join-Path $publish 'manager')

Write-Host '[2/3] Bootstrapper do WebView2 Runtime...' -ForegroundColor Green
$webView2 = Join-Path $out 'MicrosoftEdgeWebview2Setup.exe'
if (-not (Test-Path $webView2)) {
    Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $webView2 -UseBasicParsing
}

Write-Host '[3/3] WiX (MSI e bundle)...' -ForegroundColor Green
dotnet build (Join-Path $root 'installer\Bundle\Bundle.wixproj') -c Release -nologo -v q `
    "-p:Version=$Version" "-p:PublishRoot=$publish\" "-p:WebView2Bootstrapper=$webView2" -o $out
if ($LASTEXITCODE -ne 0) { throw 'Build do WiX falhou.' }

Get-ChildItem $out -File | Where-Object { $_.Name -like 'OpenWigiDash*' } |
    ForEach-Object { '{0}  {1:N1} MB' -f $_.FullName, ($_.Length / 1MB) }
Write-Host 'Pronto.' -ForegroundColor Green
