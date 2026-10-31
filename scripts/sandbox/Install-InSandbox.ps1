# Roda DENTRO do Windows Sandbox (chamado por scripts\Test-InstallerSandbox.ps1): instala o OpenWigiDash,
# confere o que o instalador deixou e grava o resultado na pasta de logs compartilhada com o PC.
# O usuário do Sandbox é admin e sem UAC, então tudo aqui roda elevado.
# Lá só tem o Windows PowerShell 5.1, que lê o arquivo como ANSI sem BOM: por isso este tem BOM.

param(
    [Parameter(Mandatory)][string]$Logs,
    [switch]$Uninstall = $false
)

$ErrorActionPreference = 'Continue'
Start-Transcript (Join-Path $Logs 'sandbox.log') | Out-Null
$results = [System.Collections.Generic.List[string]]::new()
$failed = 0

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $line = '{0}  {1}{2}' -f ($(if ($ok) { 'OK   ' } else { 'FALHA' })), $name, $(if ($detail) { " ($detail)" } else { '' })
    $results.Add($line)
    Write-Host $line -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    if (-not $ok) { $script:failed++ }
}

$programFiles = Join-Path $env:ProgramFiles 'OpenWigiDash'
$runKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$shortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\OpenWigiDash.lnk'
$webView2Key = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'

function Get-Bundle {
    Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -eq 'OpenWigiDash' -and $_.QuietUninstallString }
}

# Antes: o que um Windows limpo tem
$results.Add("Windows $([Environment]::OSVersion.Version)")
$results.Add("Antes: .NET no PATH = $([bool](Get-Command dotnet -ErrorAction SilentlyContinue)); WebView2 = $((Get-ItemProperty $webView2Key -ErrorAction SilentlyContinue).pv)")

# Instalação
$setup = Start-Process 'C:\OpenWigiDashTest\OpenWigiDash-Setup.exe' -ArgumentList '/quiet', '/log', (Join-Path $Logs 'setup.log') -Wait -PassThru
Check 'Setup.exe terminou com 0' ($setup.ExitCode -eq 0) "saída $($setup.ExitCode)"

$webView2 = (Get-ItemProperty $webView2Key -ErrorAction SilentlyContinue).pv
Check 'WebView2 Runtime presente' ([bool]$webView2) $webView2

$service = Get-CimInstance Win32_Service -Filter "Name='OpenWigiDash'"
Check 'Serviço OpenWigiDash existe' ([bool]$service)
if ($service) {
    Check 'Serviço automático' ($service.StartMode -eq 'Auto') $service.StartMode
    Check 'Serviço como LocalService' ($service.StartName -like '*LocalService*') $service.StartName
    Check 'Serviço em Program Files' ($service.PathName -like "*$programFiles\Service\OpenWigiService.exe*") $service.PathName
}
foreach ($dir in 'Service', 'Manager', 'Widgets\OpenWigi.Samples.Clock') {
    Check "Pasta $dir" (Test-Path (Join-Path $programFiles $dir))
}
Check 'Atalho no menu Iniciar' (Test-Path $shortcut)
Check 'Início com o Windows (HKLM Run)' ([bool](Get-ItemProperty $runKey -Name OpenWigiDash -ErrorAction SilentlyContinue))
$bundle = Get-Bundle
Check 'Uma entrada em Aplicativos' (@($bundle).Count -eq 1) "$(@($bundle).Count) entrada(s), versão $($bundle.DisplayVersion)"

if ($Uninstall) {
    $command = $bundle.QuietUninstallString
    $exe = $command -replace '^"([^"]+)".*$', '$1'
    $remove = Start-Process $exe -ArgumentList '/uninstall', '/quiet', '/log', (Join-Path $Logs 'uninstall.log') -Wait -PassThru
    Check 'Desinstalação terminou com 0' ($remove.ExitCode -eq 0) "saída $($remove.ExitCode)"
    Check 'Serviço removido' (-not (Get-Service OpenWigiDash -ErrorAction SilentlyContinue))
    Check 'Program Files\OpenWigiDash removido' (-not (Test-Path $programFiles))
    Check 'Atalho removido' (-not (Test-Path $shortcut))
    Check 'HKLM Run removido' (-not (Get-ItemProperty $runKey -Name OpenWigiDash -ErrorAction SilentlyContinue))
    Check 'Entrada em Aplicativos removida' (-not (Get-Bundle))
}
else {
    # Sem USB no Sandbox: o serviço passa a usar o WigiDash virtual. A sessão do Sandbox chega como
    # remota para o serviço, por isso AllowRemoteSessions também.
    $config = @{ OpenWigi = @{ Device = 'Virtual'; AllowRemoteSessions = $true } } | ConvertTo-Json -Depth 3
    Set-Content (Join-Path $programFiles 'Service\appsettings.local.json') $config -Encoding utf8
    Restart-Service OpenWigiDash
    Check 'Serviço rodando com o WigiDash virtual' ((Get-Service OpenWigiDash).Status -eq 'Running')

    $started = Get-Date
    Start-Process (Join-Path $programFiles 'Manager\OpenWigiDashManager.exe') -ArgumentList '--preview'
    Start-Sleep -Seconds 20
    Check 'Manager aberto' ([bool](Get-Process OpenWigiDashManager -ErrorAction SilentlyContinue))
    $events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'OpenWigiDash'; StartTime = $started } -ErrorAction SilentlyContinue
    Check 'Manager conectou no serviço' ([bool]($events | Where-Object Message -like 'Agente conectado*'))
    Check 'Manager ficou com a tela' ([bool]($events | Where-Object Message -like 'Posse do WigiDash*'))
    $results.Add('Confira na janela: widgets desenhando na prévia, clique = tap, segurar = long-press, arrastar = swipe.')
    $results.Add('Para testar a desinstalação à mão: Configurações > Aplicativos > OpenWigiDash.')
}

Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'OpenWigiDash' } -ErrorAction SilentlyContinue |
    Sort-Object TimeCreated | ForEach-Object { '{0:HH:mm:ss} {1}' -f $_.TimeCreated, $_.Message } |
    Set-Content (Join-Path $Logs 'eventos.txt') -Encoding utf8

$results.Insert(0, $(if ($failed -eq 0) { 'RESULTADO: tudo certo' } else { "RESULTADO: $failed falha(s)" }))
$results | Set-Content (Join-Path $Logs 'resultado.txt') -Encoding utf8
Stop-Transcript | Out-Null
