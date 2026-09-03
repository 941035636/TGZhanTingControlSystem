[CmdletBinding()]
param(
    [string]$ServerAssembly = 'src\Server\TG.Control.Server\bin\Release\net8.0\TG.Control.Server.dll',
    [ValidateRange(1, 65535)][int]$Port = 5098,
    [string]$TestRoot = (Join-Path $env:TEMP ('TG-ServerConfigTest-' + [Guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$assemblyPath = [IO.Path]::GetFullPath($ServerAssembly)
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Server assembly was not found: $assemblyPath"
}

New-Item -ItemType Directory -Path $TestRoot -Force | Out-Null
$configPath = Join-Path $TestRoot 'server.site.json'
$dataPath = Join-Path $TestRoot 'Data'
$logPath = Join-Path $TestRoot 'Logs'
$config = [ordered]@{
    Urls = "http://127.0.0.1:$Port"
    Storage = @{ DataDirectory = $dataPath }
    Terminal = @{ ApiKey = 'phase9g-runtime-test-key' }
    Admin = @{ Username = 'admin'; Password = 'phase9g-runtime-test-password'; SessionHours = 1 }
    MeloTtsLocal = @{ Enabled = $false }
    Logging = @{ FileDirectory = $logPath }
}
[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))

$stdout = Join-Path $TestRoot 'stdout.log'
$stderr = Join-Path $TestRoot 'stderr.log'
$previousConfig = $env:TG_SERVER_SITE_CONFIG
$env:TG_SERVER_SITE_CONFIG = $configPath
$processFile = if ([IO.Path]::GetExtension($assemblyPath) -eq '.exe') { $assemblyPath } else { 'dotnet.exe' }
$processArguments = if ($processFile -eq 'dotnet.exe') { @($assemblyPath) } else { @() }
$startArguments = @{
    FilePath = $processFile
    WorkingDirectory = (Split-Path $assemblyPath)
    WindowStyle = 'Hidden'
    RedirectStandardOutput = $stdout
    RedirectStandardError = $stderr
    PassThru = $true
}
if ($processArguments.Count -gt 0) { $startArguments.ArgumentList = $processArguments }
$process = Start-Process @startArguments
try {
    $health = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 250
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/api/health" -TimeoutSec 2
            if ($health.status -eq 'ok') { break }
        } catch { }
    }
    if ($null -eq $health -or $health.status -ne 'ok') {
        $details = if (Test-Path -LiteralPath $stderr) { [IO.File]::ReadAllText($stderr) } else { '' }
        throw "Server health endpoint did not become ready. $details"
    }

    $admin = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$Port/" -TimeoutSec 3
    $dailyLog = Get-ChildItem -LiteralPath $logPath -Filter 'server-*.log' -File | Select-Object -First 1
    if ($admin.StatusCode -ne 200) { throw 'Embedded AdminWeb did not return HTTP 200.' }
    if ($null -eq $dailyLog) { throw 'External log directory did not receive a daily log.' }

    Write-Host "PASS: external site config, data path, AdminWeb and daily log are active. TestRoot=$TestRoot"
} finally {
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    if ($null -ne $process) { $process.WaitForExit(); $process.Dispose() }
    $env:TG_SERVER_SITE_CONFIG = $previousConfig
}
