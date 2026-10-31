# OpenWigiDashManager - confere o pacote de plugin que `dotnet pack` gera (#45)
# Empacota o widget de exemplo e olha dentro do .nupkg: tipo de pacote, dependência do SDK,
# plugin/plugin.json e nenhum assembly que o host compartilha.

param(
    [string]$Project = (Join-Path $PSScriptRoot '..\samples\OpenWigi.Samples.Clock\OpenWigi.Samples.Clock.csproj'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$output = Join-Path ([IO.Path]::GetTempPath()) "openwigi-plugin-pack-$([guid]::NewGuid().ToString('N'))"
dotnet pack $Project -c $Configuration -o $output
if ($LASTEXITCODE -ne 0) { throw 'dotnet pack falhou.' }

$package = Get-ChildItem $output -Filter *.nupkg | Where-Object { $_.Name -notlike '*.symbols.nupkg' } | Select-Object -First 1
if (-not $package) { throw "Nenhum .nupkg em $output" }

$zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entries = $zip.Entries | ForEach-Object { $_.FullName }
    $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
    $reader = New-Object IO.StreamReader($nuspecEntry.Open())
    [xml]$nuspec = $reader.ReadToEnd()
    $reader.Dispose()
    $pluginJson = $zip.Entries | Where-Object { $_.FullName -eq 'plugin/plugin.json' } | Select-Object -First 1
    $entry = if ($pluginJson) {
        $reader = New-Object IO.StreamReader($pluginJson.Open())
        ($reader.ReadToEnd() | ConvertFrom-Json).entry
        $reader.Dispose()
    }
}
finally {
    $zip.Dispose()
}

$problems = @()
$metadata = $nuspec.package.metadata
if ($metadata.packageTypes.packageType.name -ne 'OpenWigiDashPlugin') { $problems += 'packageType não é OpenWigiDashPlugin' }
if ($metadata.tags -notmatch 'openwigidash-plugin') { $problems += 'falta a tag openwigidash-plugin' }
$sdk = $metadata.dependencies.group.dependency | Where-Object { $_.id -eq 'victorperez2911.OpenWigi.Sdk' }
if (-not $sdk) { $problems += 'falta a dependência victorperez2911.OpenWigi.Sdk' }
if (-not $entry) { $problems += 'falta plugin/plugin.json com "entry"' }
elseif ($entries -notcontains "plugin/$entry") { $problems += "plugin.json aponta para $entry, que não está no pacote" }
if ($entries | Where-Object { $_ -like 'lib/*' }) { $problems += 'há arquivos em lib/ (plugin não é biblioteca)' }
foreach ($shared in 'OpenWigi.Sdk', 'MudBlazor', 'Microsoft.AspNetCore.Components', 'Microsoft.Extensions.Logging.Abstractions') {
    if ($entries -contains "plugin/$shared.dll") { $problems += "plugin/ leva $shared.dll, que o host já entrega" }
    if ($entries | Where-Object { $_ -like "plugin/wwwroot/_content/$shared/*" }) { $problems += "plugin/ leva wwwroot/_content/$shared, que o host já entrega" }
}

Write-Host "Pacote: $($package.Name)"
Write-Host "Dependência do SDK: $($sdk.version)"
Write-Host "Conteúdo de plugin/:"
$entries | Where-Object { $_ -like 'plugin/*' } | ForEach-Object { Write-Host "  $_" }
Remove-Item $output -Recurse -Force

if ($problems) {
    $problems | ForEach-Object { Write-Host "ERRO: $_" -ForegroundColor Red }
    exit 1
}
Write-Host 'Pacote de plugin OK.' -ForegroundColor Green
