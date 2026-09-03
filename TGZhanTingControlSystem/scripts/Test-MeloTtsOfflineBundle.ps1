[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BundleRoot,
    [string]$OutputWave = (Join-Path $env:TEMP 'tg-melotts-offline-validation.wav'),
    [ValidateRange(1024, 65535)][int]$Port = 5097
)

$ErrorActionPreference = 'Stop'
$bundle = [IO.Path]::GetFullPath($BundleRoot)
$python = Join-Path $bundle 'runtime\python.exe'
$worker = Join-Path $bundle 'worker.py'
$rule = 'TG Phase9G Melo Offline Validation'
$stdout = Join-Path $env:TEMP 'tg-melotts-offline.stdout.log'
$stderr = Join-Path $env:TEMP 'tg-melotts-offline.stderr.log'
if (-not (Test-Path -LiteralPath $python -PathType Leaf)) { throw "Python runtime is missing: $python" }
if (-not (Test-Path -LiteralPath $worker -PathType Leaf)) { throw "Worker is missing: $worker" }

$firewallActive = $false
& "$env:SystemRoot\System32\netsh.exe" advfirewall firewall delete rule name="$rule" 2>$null | Out-Null
& "$env:SystemRoot\System32\netsh.exe" advfirewall firewall add rule name="$rule" dir=out action=block program="$python" enable=yes profile=any 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { $firewallActive = $true }

$previousEnvironment = @{
    HF_HUB_OFFLINE = $env:HF_HUB_OFFLINE; TRANSFORMERS_OFFLINE = $env:TRANSFORMERS_OFFLINE
    HF_DATASETS_OFFLINE = $env:HF_DATASETS_OFFLINE; HTTP_PROXY = $env:HTTP_PROXY; HTTPS_PROXY = $env:HTTPS_PROXY
    NO_PROXY = $env:NO_PROXY
}
$env:HF_HUB_OFFLINE = '1'
$env:TRANSFORMERS_OFFLINE = '1'
$env:HF_DATASETS_OFFLINE = '1'
$env:HTTP_PROXY = 'http://127.0.0.1:9'
$env:HTTPS_PROXY = 'http://127.0.0.1:9'
$env:NO_PROXY = '127.0.0.1,localhost'

$arguments = @(
    $worker, '--host', '127.0.0.1', '--port', [string]$Port,
    '--melotts-source', (Join-Path $bundle 'vendor\MeloTTS'),
    '--acoustic-model', (Join-Path $bundle 'models\MeloTTS-Chinese'),
    '--bert-model', (Join-Path $bundle 'models\bert-base-multilingual-uncased'),
    '--nltk-data', (Join-Path $bundle 'runtime\nltk_data')
)
$process = $null
try {
    $process = Start-Process -FilePath $python -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $deadline = [DateTimeOffset]::Now.AddSeconds(120)
    $ready = $false
    while ([DateTimeOffset]::Now -lt $deadline -and -not $process.HasExited) {
        try {
            $health = Invoke-RestMethod "http://127.0.0.1:$Port/health" -TimeoutSec 2
            if ($health.available) { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw "Worker did not become ready. stderr: $(Get-Content $stderr -Tail 8 -ErrorAction SilentlyContinue)" }

    $narrationText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(
        '5qyi6L+O5p2l5Yiw5pm65oWn5bGV5Y6F44CC5b2T5YmN6K+t6Z+z55Sx5q2j5byPTWVsb1RUU+emu+e6v+i/kOihjOaXtueUn+aIkO+8jOWFqOeoi+S4jeiuv+mXruS6kuiBlOe9keOAgg=='))
    $body = @{
        requestId = 'phase9g-offline'; text = $narrationText
        voice = 'zh-standard'; language = 'zh-CN'; rate = 1.0; pitch = 0.0; volume = 1.0
        outputMediaType = 'audio/wav'; sampleRateHz = 44100; channels = 1
    } | ConvertTo-Json
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Invoke-WebRequest "http://127.0.0.1:$Port/synthesize" -Method Post -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($body)) -OutFile $OutputWave -TimeoutSec 300
    $watch.Stop()

    $bytes = [IO.File]::ReadAllBytes($OutputWave)
    $channels = [BitConverter]::ToInt16($bytes, 22)
    $sampleRate = [BitConverter]::ToInt32($bytes, 24)
    $bits = [BitConverter]::ToInt16($bytes, 34)
    $duration = ($bytes.Length - 44) / ($sampleRate * $channels * ($bits / 8))
    [pscustomobject]@{
        Result = 'PASS'; IsolationMode = $(if ($firewallActive) { 'WindowsFirewallOutboundBlock' } else { 'OfflineEnvironmentAndDeadProxy' })
        OfflineFirewallRule = $firewallActive; ElapsedSeconds = [math]::Round($watch.Elapsed.TotalSeconds, 2)
        DurationSeconds = [math]::Round($duration, 2); SampleRate = $sampleRate; Channels = $channels
        BitsPerSample = $bits; Size = $bytes.Length
        Sha256 = (Get-FileHash -LiteralPath $OutputWave -Algorithm SHA256).Hash.ToLowerInvariant()
        WavePath = [IO.Path]::GetFullPath($OutputWave)
    }
} finally {
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    if ($firewallActive) { & "$env:SystemRoot\System32\netsh.exe" advfirewall firewall delete rule name="$rule" | Out-Null }
    $env:HF_HUB_OFFLINE = $previousEnvironment.HF_HUB_OFFLINE
    $env:TRANSFORMERS_OFFLINE = $previousEnvironment.TRANSFORMERS_OFFLINE
    $env:HF_DATASETS_OFFLINE = $previousEnvironment.HF_DATASETS_OFFLINE
    $env:HTTP_PROXY = $previousEnvironment.HTTP_PROXY
    $env:HTTPS_PROXY = $previousEnvironment.HTTPS_PROXY
    $env:NO_PROXY = $previousEnvironment.NO_PROXY
}
