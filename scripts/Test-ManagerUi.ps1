# OpenWigiDashManager - testes da UI do manager pelo WebView2 (Playwright via CDP)
# Abre o manager com a porta de depuração do WebView2 e roda os scripts de tests/ui contra ele.
# Precisa do serviço rodando e de widgets instalados (scripts\Deploy-Widget.ps1).

param([int]$Port = 9333)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$ui = Join-Path $root 'tests\ui'
$exe = Get-ChildItem (Join-Path $root 'src\OpenWigi.Manager\bin\Debug') -Recurse -Filter OpenWigiDashManager.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw 'Compile o manager antes: dotnet build src\OpenWigi.Manager' }

Get-Process OpenWigiDashManager -ErrorAction SilentlyContinue | Stop-Process -Force
# O WebView2 do manager anterior demora a sair; se ainda estiver vivo, o novo reaproveita e ignora a porta.
Start-Sleep -Seconds 4
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$Port"
$env:OWD_CDP_PORT = $Port
Start-Process $exe.FullName -ArgumentList '--open', '/widgets'
Start-Sleep -Seconds 12

if (-not (Test-Path (Join-Path $ui 'node_modules\playwright'))) {
    $env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = '1'   # só conecta no WebView2, não baixa navegador
    npm install --prefix $ui --no-audit --no-fund | Out-Null
}
$shot = Join-Path $root '.diagnostics\ui-drag.png'
New-Item -ItemType Directory (Split-Path $shot) -Force | Out-Null
node (Join-Path $ui 'drag-and-drop.mjs') $shot
if ($LASTEXITCODE -ne 0) { throw 'Teste de arrastar falhou.' }
Write-Host "Captura do arraste: $shot" -ForegroundColor Cyan
