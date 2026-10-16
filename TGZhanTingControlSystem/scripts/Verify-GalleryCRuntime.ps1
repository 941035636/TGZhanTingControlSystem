[CmdletBinding()]
param(
    [string]$PlayerPath,
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildDirectory = Join-Path $repoRoot 'Builds'
if ([string]::IsNullOrWhiteSpace($PlayerPath)) {
    $PlayerPath = Join-Path $buildDirectory 'GalleryC-LedPlayer\TG.Control.LedPlayer.exe'
}
$serverDll = Join-Path $repoRoot 'src\Server\TG.Control.Server\bin\Release\net8.0\TG.Control.Server.dll'
$firstSource = Join-Path $repoRoot 'src\TouchClient\Assets\Resources\TGExhibitionUI\HomeDark\Backgrounds\Home_Dark_Abstract_1920x1080.png'
$secondSource = Join-Path $repoRoot 'src\TouchClient\Assets\Resources\TGExhibitionUI\HomeDark\Icons\nav_home_128.png'
foreach ($path in @($PlayerPath, $serverDll, $firstSource, $secondSource)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Gallery C runtime dependency not found: $path" }
}
if (Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Port 5080 is already in use. Stop the existing Server before running Gallery C runtime verification.'
}
if (Get-Process 'TG.Control.LedPlayer' -ErrorAction SilentlyContinue) {
    throw 'TG.Control.LedPlayer is already running. Stop it before running Gallery C runtime verification.'
}

$runId = [Guid]::NewGuid().ToString('N')
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "TG.Control.GalleryC.Runtime.$runId"
$temporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
$expectedTemporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not $temporaryRoot.StartsWith($expectedTemporaryPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not ([IO.Path]::GetFileName($temporaryRoot)).StartsWith('TG.Control.GalleryC.Runtime.', [StringComparison]::Ordinal)) {
    throw "Refusing unsafe Gallery C temporary directory: $temporaryRoot"
}
$dataDirectory = Join-Path $temporaryRoot 'Data'
$mediaDirectory = Join-Path $dataDirectory 'Media'
$serverStdout = Join-Path $buildDirectory 'GalleryC-Runtime-Server.stdout.log'
$serverStderr = Join-Path $buildDirectory 'GalleryC-Runtime-Server.stderr.log'
$playerLog = Join-Path $buildDirectory 'GalleryC-Runtime-LedPlayer.log'
$serverProcess = $null
$playerProcess = $null
$savedEnvironment = @{
    ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT
    Storage__DataDirectory = $env:Storage__DataDirectory
    Terminal__ApiKey = $env:Terminal__ApiKey
    Urls = $env:Urls
    TG_LED_PLAYER_CONFIG = $env:TG_LED_PLAYER_CONFIG
}

function Wait-Until([scriptblock]$Condition, [string]$FailureMessage) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try { if (& $Condition) { return } } catch { }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw $FailureMessage
}

try {
    New-Item -ItemType Directory -Path $mediaDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
    $firstName = 'gallery-c-a.png'
    $secondName = 'gallery-c-b.png'
    $firstPath = Join-Path $mediaDirectory $firstName
    $secondPath = Join-Path $mediaDirectory $secondName
    Copy-Item -LiteralPath $firstSource -Destination $firstPath
    Copy-Item -LiteralPath $secondSource -Destination $secondPath

    $firstFile = Get-Item -LiteralPath $firstPath
    $secondFile = Get-Item -LiteralPath $secondPath
    $firstHash = (Get-FileHash -LiteralPath $firstPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $secondHash = (Get-FileHash -LiteralPath $secondPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $content = [ordered]@{
        version = 1
        publishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        publishedBy = 'gallery-c-runtime-verification'
        modules = @([ordered]@{
            id = 'module-gallery-c'
            name = 'Gallery C Runtime'
            order = 1
            description = ''
            coverUrl = $null
            enabled = $true
            nodes = @()
            galleryImages = @(
                [ordered]@{ id = 'gallery-c-a'; name = 'Image A'; kind = 1; url = "/media/$firstName"; sha256 = $firstHash; sizeBytes = $firstFile.Length; durationSeconds = 0; mediaType = 'image/png' },
                [ordered]@{ id = 'gallery-c-b'; name = 'Image B'; kind = 1; url = "/media/$secondName"; sha256 = $secondHash; sizeBytes = $secondFile.Length; durationSeconds = 0; mediaType = 'image/png' }
            )
        })
    }
    [IO.File]::WriteAllText((Join-Path $dataDirectory 'published-content.json'),
        ($content | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    $playerConfigPath = Join-Path $temporaryRoot 'led-player.json'
    $playerCachePath = Join-Path $temporaryRoot 'LedCache'
    $playerConfig = [ordered]@{
        serverBaseUrl = 'http://127.0.0.1:5080'
        clientId = 'led-main'
        terminalApiKey = 'TG-DEVELOPMENT-ONLY'
        cacheDirectory = $playerCachePath
    }
    [IO.File]::WriteAllText($playerConfigPath, ($playerConfig | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    New-Item -ItemType Directory -Path $playerCachePath -Force | Out-Null
    $firstMediaUrl = 'http://127.0.0.1:5080/media/gallery-c-a.png'
    $urlHasher = [Security.Cryptography.SHA256]::Create()
    try {
        $cacheKey = ([BitConverter]::ToString($urlHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($firstMediaUrl)))).Replace('-', '').ToLowerInvariant()
    }
    finally { $urlHasher.Dispose() }
    # Same-size corrupt content verifies that Gallery Resolve performs SHA validation
    # even when the background manifest scan accepted the file length.
    $cachedFirstPath = Join-Path $playerCachePath ($cacheKey + '.png')
    [IO.File]::WriteAllBytes($cachedFirstPath, [byte[]]::new($firstFile.Length))

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Storage__DataDirectory = $dataDirectory
    $env:Terminal__ApiKey = 'TG-DEVELOPMENT-ONLY'
    $env:Urls = 'http://127.0.0.1:5080'
    $env:TG_LED_PLAYER_CONFIG = $playerConfigPath
    $serverProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($serverDll) -WorkingDirectory (Split-Path $serverDll) `
        -RedirectStandardOutput $serverStdout -RedirectStandardError $serverStderr -WindowStyle Hidden -PassThru

    Wait-Until { (Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/health').status -eq 'ok' } `
        'Server did not become healthy.'

    $playerProcess = Start-Process -FilePath $PlayerPath -ArgumentList @(
        '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '360', '-logFile', $playerLog
    ) -WindowStyle Hidden -PassThru

    $headers = @{ 'X-TG-Terminal-Key' = 'TG-DEVELOPMENT-ONLY' }
    Wait-Until { (Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/readiness' -Headers $headers).ledOnline } `
        'LedPlayer did not register with the Server.'

    $startBody = @{ moduleId = 'module-gallery-c'; imageIndex = 0; requestedBy = 'gallery-c-runtime-verification' } | ConvertTo-Json
    $started = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5080/api/gallery/start' -Headers $headers `
        -ContentType 'application/json' -Body $startBody
    Wait-Until {
        $lookup = Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/gallery/active' -Headers $headers
        $lookup.active -and $lookup.session.state -eq 1 -and $lookup.session.imageId -eq 'gallery-c-a'
    } 'LedPlayer did not decode and report Image A as Showing.'

    $nextBody = @{ sessionId = $started.sessionId; action = 2; imageIndex = $null; slideshowIntervalSeconds = $null } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5080/api/gallery/control' -Headers $headers `
        -ContentType 'application/json' -Body $nextBody | Out-Null
    Wait-Until {
        $lookup = Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/gallery/active' -Headers $headers
        $lookup.active -and $lookup.session.state -eq 1 -and $lookup.session.imageId -eq 'gallery-c-b'
    } 'LedPlayer did not complete the double-buffer transition to Image B.'

    $stopBody = @{ sessionId = $started.sessionId; action = 5; imageIndex = $null; slideshowIntervalSeconds = $null } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5080/api/gallery/control' -Headers $headers `
        -ContentType 'application/json' -Body $stopBody | Out-Null
    Wait-Until { -not (Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/gallery/active' -Headers $headers).active } `
        'Gallery session did not stop cleanly.'

    if (-not (Test-Path -LiteralPath $cachedFirstPath) -or
        (Get-FileHash -LiteralPath $cachedFirstPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $firstHash) {
        throw 'Corrupt same-size cache was not detected and redownloaded.'
    }
    Write-Output 'PASS Gallery C Windows runtime: corrupt cache recovery -> Prepare -> Ready -> Show Image A -> Next Image B -> Stop.'
    Write-Output "Player log: $playerLog"
    Write-Output "Server log: $serverStdout"
}
finally {
    if ($playerProcess -and -not $playerProcess.HasExited) { Stop-Process -Id $playerProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($serverProcess -and -not $serverProcess.HasExited) { Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue }
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
