# OpenWigiDashManager - checagem no aparelho real
# Compila, libera o WigiDash do manager oficial, roda o owd contra o hardware e
# (com -RestoreOfficial) devolve o aparelho ao manager oficial no fim.

param(
    [string]$Configuration = "Debug",
    [switch]$StopOfficial = $false,
    [switch]$RestoreOfficial = $false,
    [switch]$Touch = $false,
    [int]$TouchSeconds = 20,
    [switch]$SkipBuild = $false
)

$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$owd = Join-Path $root "src\OpenWigi.Cli\bin\$Configuration\net10.0-windows\owd.exe"
$officialExe = "C:\Program Files (x86)\G.SKILL\WigiDash Manager\WigiDashManager.exe"
$diagnostics = Join-Path $root ".diagnostics\device-check"

Write-Host "=== OpenWigiDashManager - checagem no aparelho ===" -ForegroundColor Cyan

# Passo 1: build
Write-Host "[1/4] Build ($Configuration)..." -ForegroundColor Green
if (-not $SkipBuild) {
    dotnet build (Join-Path $root 'OpenWigiDashManager.slnx') -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build falhou.' }
}
if (-not (Test-Path -LiteralPath $owd)) { throw "owd não encontrado: $owd" }

# Passo 2: só um processo abre o WinUSB por vez
Write-Host "[2/4] Verificando o manager oficial..." -ForegroundColor Green
$official = Get-Process -Name 'WigiDashManager' -ErrorAction SilentlyContinue
if ($official) {
    if (-not $StopOfficial) {
        throw 'O manager oficial está com o aparelho. Rode com -StopOfficial para fechá-lo.'
    }
    Write-Host 'Fechando o manager oficial para liberar o USB...' -ForegroundColor Yellow
    $official | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# Passo 3: checagens no hardware, com log para comparar entre execuções
Write-Host "[3/4] Rodando o owd no aparelho..." -ForegroundColor Green
New-Item -ItemType Directory -Path $diagnostics -Force | Out-Null
$log = Join-Path $diagnostics ("{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
$failed = $false
try {
    foreach ($step in @(@('list'), @('info'), @('draw', 'pattern'), @('draw', 'pattern', '--page', '1', '--band'), @('page', '0'))) {
        "> owd $($step -join ' ')" | Tee-Object -FilePath $log -Append
        & $owd @step 2>&1 | Tee-Object -FilePath $log -Append
        if ($LASTEXITCODE -ne 0) { $failed = $true; break }
    }

    if ($Touch -and -not $failed) {
        Write-Host "Toque na tela por $TouchSeconds s: tap, swipe e segurar (long-press)." -ForegroundColor Yellow
        $touchLog = Join-Path $diagnostics 'touch.txt'
        $process = Start-Process $owd -ArgumentList 'touch', '--raw' -NoNewWindow -PassThru -RedirectStandardOutput $touchLog
        Start-Sleep -Seconds $TouchSeconds
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Get-Content $touchLog | Tee-Object -FilePath $log -Append
    }
}
finally {
    # Passo 4: devolve o aparelho
    if ($RestoreOfficial -and (Test-Path -LiteralPath $officialExe)) {
        Write-Host "[4/4] Religando o manager oficial..." -ForegroundColor Green
        Start-Process $officialExe -WindowStyle Hidden
    }
    else {
        Write-Host "[4/4] Manager oficial continua fechado (use -RestoreOfficial para religar)." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Log: $log" -ForegroundColor Cyan
if ($failed) { throw 'Alguma checagem falhou; veja o log.' }
Write-Host "=== Checagem concluída ===" -ForegroundColor Green
