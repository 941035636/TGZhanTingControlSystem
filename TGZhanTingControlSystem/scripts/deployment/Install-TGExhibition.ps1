[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [string]$DataRoot = (Join-Path $env:ProgramData 'TG Exhibition'),
    [ValidateRange(1, 65535)][int]$ServerPort = 5080
)

$ErrorActionPreference = 'Stop'
$serviceName = 'TG Exhibition Control Server'
$firewallRuleName = 'TG Exhibition Server API'
$runValueName = 'TG Exhibition Launcher'
$install = [IO.Path]::GetFullPath($InstallRoot)
$data = [IO.Path]::GetFullPath($DataRoot)

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Installation must run from an elevated administrator process.'
    }
}

function New-RandomSecret([int]$byteCount) {
    $bytes = New-Object byte[] $byteCount
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return ([Convert]::ToBase64String($bytes).TrimEnd('=') -replace '\+', '-' -replace '/', '_')
}

function Write-JsonIfMissing([string]$path, [object]$value) {
    if (Test-Path -LiteralPath $path) { return $false }
    $json = $value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
    return $true
}

function Invoke-Sc([string[]]$arguments, [switch]$AllowFailure) {
    & "$env:SystemRoot\System32\sc.exe" @arguments | Out-Null
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        throw "sc.exe failed with exit code ${LASTEXITCODE}: $($arguments -join ' ')"
    }
}

Assert-Administrator
if (-not (Test-Path -LiteralPath $install)) { throw "Install root does not exist: $install" }
$serverExe = Join-Path $install 'Server\TG.Control.Server.exe'
$launcherExe = Join-Path $install 'Launcher\TG.Control.Launcher.exe'
if (-not (Test-Path -LiteralPath $serverExe)) { throw "Server executable is missing: $serverExe" }
if (-not (Test-Path -LiteralPath $launcherExe)) { throw "Launcher executable is missing: $launcherExe" }

$directories = @('Config','Data','Media','Cache','Logs','Backups','Runtime')
foreach ($name in $directories) { New-Item -ItemType Directory -Path (Join-Path $data $name) -Force | Out-Null }
New-Item -ItemType Directory -Path `
    (Join-Path $data 'Logs\Server'),(Join-Path $data 'Logs\Launcher'),
    (Join-Path $data 'Logs\TouchClient'),(Join-Path $data 'Logs\LedPlayer'),
    (Join-Path $data 'Cache\LedPlayer') -Force | Out-Null

$configDirectory = Join-Path $data 'Config'
$existingConfigFiles = Get-ChildItem -LiteralPath $configDirectory -File -ErrorAction SilentlyContinue
if ($existingConfigFiles) {
    $backupDirectory = Join-Path $data ('Backups\preinstall-' + [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backupDirectory | Out-Null
    foreach ($file in $existingConfigFiles) { Copy-Item -LiteralPath $file.FullName -Destination $backupDirectory }
}

$terminalKey = New-RandomSecret 32
$adminPassword = New-RandomSecret 18
$serverBaseUrl = "http://127.0.0.1:$ServerPort"
$serverConfig = [ordered]@{
    Urls = "http://0.0.0.0:$ServerPort"
    Storage = @{ DataDirectory = (Join-Path $data 'Data') }
    Playback = @{
        TouchClientId = 'touch-main'; LedClientId = 'led-main'; PrepareLeadMilliseconds = 1500
        SyncToleranceMilliseconds = 500; LongPollSeconds = 20; RequireLedReadyBeforeStart = $true
        AllowDegradedPlayback = $true
    }
    Terminal = @{ ApiKey = $terminalKey }
    Admin = @{ Username = 'admin'; Password = $adminPassword; SessionHours = 12 }
    TtsProduction = @{
        EnableDeterministicTestProvider = $false; MaxTextLength = 5000; MaxAttempts = 3
        AttemptTimeoutMilliseconds = 300000; RetryDelayMilliseconds = 250
        MinAudioSizeBytes = 45; MaxAudioSizeBytes = 104857600
    }
    MeloTtsLocal = @{
        Enabled = $true; AutoStartWorker = $true; BaseAddress = 'http://127.0.0.1:5091'
        PythonExecutablePath = (Join-Path $install 'TtsWorker\MeloTtsLocal\runtime\python.exe')
        WorkerScriptPath = (Join-Path $install 'TtsWorker\MeloTtsLocal\worker.py')
        MeloTtsSourcePath = (Join-Path $install 'TtsWorker\MeloTtsLocal\vendor\MeloTTS')
        AcousticModelPath = (Join-Path $install 'TtsWorker\MeloTtsLocal\models\MeloTTS-Chinese')
        BertModelPath = (Join-Path $install 'TtsWorker\MeloTtsLocal\models\bert-base-multilingual-uncased')
        NltkDataPath = (Join-Path $install 'TtsWorker\MeloTtsLocal\runtime\nltk_data')
        HealthTimeoutMilliseconds = 2500; RestartDelayMilliseconds = 5000
    }
    Logging = @{ FileDirectory = (Join-Path $data 'Logs\Server'); LogLevel = @{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning' } }
}
$serverCreated = Write-JsonIfMissing (Join-Path $configDirectory 'server.site.json') $serverConfig

$effectiveServerConfig = Get-Content -LiteralPath (Join-Path $configDirectory 'server.site.json') -Raw | ConvertFrom-Json
$effectiveTerminalKey = [string]$effectiveServerConfig.Terminal.ApiKey
if ([string]::IsNullOrWhiteSpace($effectiveTerminalKey)) { throw 'The existing server.site.json has no Terminal.ApiKey.' }

Write-JsonIfMissing (Join-Path $configDirectory 'touch-client.json') ([ordered]@{
    serverBaseUrl = $serverBaseUrl; clientId = 'touch-main'; terminalApiKey = $effectiveTerminalKey
}) | Out-Null
Write-JsonIfMissing (Join-Path $configDirectory 'led-player.json') ([ordered]@{
    serverBaseUrl = $serverBaseUrl; clientId = 'led-main'; terminalApiKey = $effectiveTerminalKey
    cacheDirectory = (Join-Path $data 'Cache\LedPlayer\Content')
}) | Out-Null
Write-JsonIfMissing (Join-Path $configDirectory 'launcher.json') ([ordered]@{
    serverHealthUrl = "$serverBaseUrl/api/health"; adminUrl = "$serverBaseUrl/"
    touchClientExecutable = (Join-Path $install 'TouchClient\TouchClient.exe')
    ledPlayerExecutable = (Join-Path $install 'LedPlayer\LedPlayer.exe')
    touchClientConfiguration = (Join-Path $configDirectory 'touch-client.json')
    ledPlayerConfiguration = (Join-Path $configDirectory 'led-player.json')
    touchClientLogFile = (Join-Path $data 'Logs\TouchClient\Player.log')
    ledPlayerLogFile = (Join-Path $data 'Logs\LedPlayer\Player.log')
    logDirectory = (Join-Path $data 'Logs\Launcher'); healthPollSeconds = 3
    clientRestartDelaySeconds = 5; autoStartTouchClient = $true; autoStartLedPlayer = $true
    autoRestartClients = $true
}) | Out-Null

if ($serverCreated) {
    $credentialsPath = Join-Path $configDirectory 'initial-credentials.txt'
    $credentialText = "Admin URL: $serverBaseUrl/`r`nUsername: admin`r`nInitial password: $adminPassword`r`n"
    [IO.File]::WriteAllText($credentialsPath, $credentialText, [Text.UTF8Encoding]::new($false))
}

# Program Files remains read-only at runtime. Kiosk users may traverse the config directory, but only the two
# terminal configs are readable by them. Server credentials and the first-install credential note remain restricted.
& "$env:SystemRoot\System32\icacls.exe" $configDirectory '/inheritance:r' '/grant:r' `
    'SYSTEM:(OI)(CI)F' 'Administrators:(OI)(CI)F' 'Users:(RX)' | Out-Null
$serverConfigPath = Join-Path $configDirectory 'server.site.json'
& "$env:SystemRoot\System32\icacls.exe" $serverConfigPath '/inheritance:r' '/grant:r' `
    'SYSTEM:F' 'Administrators:F' | Out-Null
foreach ($terminalConfigName in @('touch-client.json','led-player.json','launcher.json')) {
    & "$env:SystemRoot\System32\icacls.exe" (Join-Path $configDirectory $terminalConfigName) `
        '/inheritance:r' '/grant:r' 'SYSTEM:F' 'Administrators:F' 'Users:R' | Out-Null
}
$credentialsPath = Join-Path $configDirectory 'initial-credentials.txt'
if (Test-Path -LiteralPath $credentialsPath) {
    & "$env:SystemRoot\System32\icacls.exe" $credentialsPath '/inheritance:r' '/grant:r' `
        'SYSTEM:F' 'Administrators:F' | Out-Null
}
foreach ($writableName in @('Cache','Logs','Runtime')) {
    & "$env:SystemRoot\System32\icacls.exe" (Join-Path $data $writableName) '/inheritance:r' '/grant:r' 'SYSTEM:(OI)(CI)F' 'Administrators:(OI)(CI)F' 'Users:(OI)(CI)M' | Out-Null
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force; $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }
    Invoke-Sc @('delete', $serviceName)
    for ($attempt = 0; $attempt -lt 30 -and (Get-Service -Name $serviceName -ErrorAction SilentlyContinue); $attempt++) {
        Start-Sleep -Milliseconds 500
    }
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        throw 'The previous Server service could not be removed before registration.'
    }
}
New-Service -Name $serviceName -BinaryPathName ('"' + $serverExe + '"') `
    -DisplayName 'TG Exhibition Control Server' -Description 'TG Exhibition content, playback coordination, and local TTS service' `
    -StartupType Automatic | Out-Null
Invoke-Sc @('failure', $serviceName, 'reset=', '86400', 'actions=', 'restart/5000/restart/10000/restart/30000')
Invoke-Sc @('failureflag', $serviceName, '1')

& "$env:SystemRoot\System32\netsh.exe" advfirewall firewall delete rule name="$firewallRuleName" | Out-Null
& "$env:SystemRoot\System32\netsh.exe" advfirewall firewall add rule name="$firewallRuleName" dir=in action=allow protocol=TCP localport=$ServerPort profile=private program="$serverExe" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not create Windows Firewall rule (exit $LASTEXITCODE)." }

$runKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
New-ItemProperty -Path $runKey -Name $runValueName -Value ('"' + $launcherExe + '"') -PropertyType String -Force | Out-Null
Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
Write-Host 'TG Exhibition production runtime registration completed.'
