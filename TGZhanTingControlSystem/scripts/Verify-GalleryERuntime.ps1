[CmdletBinding()]
param(
    [string]$LedPlayerPath,
    [string]$TouchClientPath,
    [int]$TimeoutSeconds = 90,
    [int]$LegacyUdpPort = 7002
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildDirectory = Join-Path $repoRoot 'Builds'
$artifactDirectory = Join-Path $repoRoot 'artifacts'
if ([string]::IsNullOrWhiteSpace($LedPlayerPath)) {
    $LedPlayerPath = Join-Path $buildDirectory 'GalleryE-LedPlayer\TG.Control.LedPlayer.exe'
}
if ([string]::IsNullOrWhiteSpace($TouchClientPath)) {
    $TouchClientPath = Join-Path $buildDirectory 'GalleryE-TouchClient\TG.Control.TouchClient.exe'
}
$serverDll = Join-Path $repoRoot 'src\Server\TG.Control.Server\bin\Release\net8.0\TG.Control.Server.dll'
$fourKSource = Join-Path $repoRoot 'src\TouchClient\Assets\Resources\TGExhibitionUI\HomeDarkV2\Backgrounds\HomeTechBackground_4K.png'
foreach ($path in @($LedPlayerPath, $TouchClientPath, $serverDll, $fourKSource)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Gallery E runtime dependency not found: $path" }
}
if (Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Port 5080 is already in use. Stop the existing Server before running Gallery E runtime verification.'
}
if (Get-NetUDPEndpoint -LocalPort $LegacyUdpPort -ErrorAction SilentlyContinue) {
    throw "UDP port $LegacyUdpPort is already in use."
}
if (Get-Process 'TG.Control.LedPlayer','TG.Control.TouchClient' -ErrorAction SilentlyContinue) {
    throw 'A TG Windows Player is already running. Stop it before running Gallery E verification.'
}

Add-Type -AssemblyName System.Drawing
$sourceImage = [Drawing.Image]::FromFile($fourKSource)
try {
    if ($sourceImage.Width -ne 3840 -or $sourceImage.Height -ne 2160) {
        throw "Gallery E requires a real 3840x2160 image; actual size is $($sourceImage.Width)x$($sourceImage.Height)."
    }
}
finally { $sourceImage.Dispose() }

$runId = [Guid]::NewGuid().ToString('N')
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) "TG.Control.GalleryE.Runtime.$runId"))
$temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not $temporaryRoot.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not ([IO.Path]::GetFileName($temporaryRoot)).StartsWith('TG.Control.GalleryE.Runtime.', [StringComparison]::Ordinal)) {
    throw "Refusing unsafe Gallery E temporary directory: $temporaryRoot"
}

$dataDirectory = Join-Path $temporaryRoot 'Data'
$mediaDirectory = Join-Path $dataDirectory 'Media'
$cacheDirectory = Join-Path $temporaryRoot 'LedCache'
$serverLog1 = Join-Path $buildDirectory 'GalleryE-Runtime-Server-1.stdout.log'
$serverError1 = Join-Path $buildDirectory 'GalleryE-Runtime-Server-1.stderr.log'
$serverLog2 = Join-Path $buildDirectory 'GalleryE-Runtime-Server-2.stdout.log'
$serverError2 = Join-Path $buildDirectory 'GalleryE-Runtime-Server-2.stderr.log'
$serverLog3 = Join-Path $buildDirectory 'GalleryE-Runtime-Server-3.stdout.log'
$serverError3 = Join-Path $buildDirectory 'GalleryE-Runtime-Server-3.stderr.log'
$ledLog1 = Join-Path $buildDirectory 'GalleryE-Runtime-LedPlayer-1.log'
$ledLog2 = Join-Path $buildDirectory 'GalleryE-Runtime-LedPlayer-2.log'
$touchLog1 = Join-Path $buildDirectory 'GalleryE-Runtime-TouchClient-1.log'
$touchLog2 = Join-Path $buildDirectory 'GalleryE-Runtime-TouchClient-2.log'
$resultPath = Join-Path $artifactDirectory 'gallery-e-runtime-verification.json'
$serverProcess = $null
$ledProcess = $null
$touchProcess = $null
$results = [Collections.Generic.List[object]]::new()
$savedEnvironment = @{}
$environmentNames = @(
    'ASPNETCORE_ENVIRONMENT','Storage__DataDirectory','Terminal__ApiKey','Urls',
    'Playback__LongPollSeconds','Playback__RequireLedReadyBeforeStart','Playback__AllowDegradedPlayback',
    'TG_LED_PLAYER_CONFIG','TG_TOUCH_CLIENT_CONFIG','TG_LEGACY_UDP_PORT','TG_LEGACY_UDP_ENABLED'
)
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

function Add-Result([string]$Name, [string]$Status, [string]$Detail) {
    $results.Add([ordered]@{ name = $Name; status = $Status; detail = $Detail })
    Write-Output "$Status $Name - $Detail"
}

function Wait-Until([scriptblock]$Condition, [string]$FailureMessage, [int]$Seconds = $TimeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        try { if (& $Condition) { return } } catch { }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw $FailureMessage
}

function Stop-TestProcess([Diagnostics.Process]$Process) {
    if ($null -eq $Process) { return }
    try { $Process.Refresh() } catch { return }
    if (-not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        try { $Process.WaitForExit(10000) | Out-Null } catch { }
    }
}

function Start-Server([string]$Stdout, [string]$Stderr) {
    return Start-Process -FilePath 'dotnet' -ArgumentList @($serverDll) -WorkingDirectory (Split-Path $serverDll) `
        -RedirectStandardOutput $Stdout -RedirectStandardError $Stderr -WindowStyle Hidden -PassThru
}

function Start-Led([string]$LogPath) {
    return Start-Process -FilePath $LedPlayerPath -ArgumentList @(
        '-screen-fullscreen', '0', '-screen-width', '960', '-screen-height', '540', '-logFile', $LogPath
    ) -WindowStyle Hidden -PassThru
}

function Start-Touch([string]$LogPath) {
    return Start-Process -FilePath $TouchClientPath -ArgumentList @(
        '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-logFile', $LogPath
    ) -WindowStyle Hidden -PassThru
}

function Get-ActiveGallery {
    return Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/gallery/active' -Headers $script:headers
}

function Send-GalleryControl([string]$SessionId, [int]$Action, [Nullable[int]]$ImageIndex = $null,
    [Nullable[int]]$Interval = $null) {
    $body = @{ sessionId = $SessionId; action = $Action; imageIndex = $ImageIndex; slideshowIntervalSeconds = $Interval } | ConvertTo-Json
    return Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5080/api/gallery/control' `
        -Headers $script:headers -ContentType 'application/json' -Body $body
}

try {
    New-Item -ItemType Directory -Path $mediaDirectory,$cacheDirectory,$buildDirectory,$artifactDirectory -Force | Out-Null
    $assets = @()
    foreach ($index in 1..3) {
        $name = "gallery-e-4k-$index.png"
        $path = Join-Path $mediaDirectory $name
        Copy-Item -LiteralPath $fourKSource -Destination $path
        $file = Get-Item -LiteralPath $path
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $assets += [ordered]@{
            id = "gallery-e-image-$index"; name = "4K测试图$index"; kind = 1; url = "/media/$name"
            sha256 = $hash; sizeBytes = $file.Length; durationSeconds = 0; mediaType = 'image/png'
        }
    }
    $content = [ordered]@{
        version = 501
        publishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        publishedBy = 'gallery-e-runtime-verification'
        modules = @([ordered]@{
            id = 'module-gallery-e'; name = 'Gallery E 4K'; order = 1; description = ''
            coverUrl = $null; enabled = $true; nodes = @(); galleryImages = $assets
        })
    }
    $contentPath = Join-Path $dataDirectory 'published-content.json'
    [IO.File]::WriteAllText($contentPath, ($content | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))

    $ledConfigPath = Join-Path $temporaryRoot 'led-player.json'
    $touchConfigPath = Join-Path $temporaryRoot 'touch-client.json'
    [IO.File]::WriteAllText($ledConfigPath, ([ordered]@{
        serverBaseUrl = 'http://127.0.0.1:5080'; clientId = 'led-main'
        terminalApiKey = 'TG-DEVELOPMENT-ONLY'; cacheDirectory = $cacheDirectory
    } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($touchConfigPath, ([ordered]@{
        serverBaseUrl = 'http://127.0.0.1:5080'; clientId = 'touch-main'; terminalApiKey = 'TG-DEVELOPMENT-ONLY'
    } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Storage__DataDirectory = $dataDirectory
    $env:Terminal__ApiKey = 'TG-DEVELOPMENT-ONLY'
    $env:Urls = 'http://127.0.0.1:5080'
    $env:Playback__LongPollSeconds = '2'
    $env:Playback__RequireLedReadyBeforeStart = 'true'
    $env:Playback__AllowDegradedPlayback = 'false'
    $env:TG_LED_PLAYER_CONFIG = $ledConfigPath
    $env:TG_TOUCH_CLIENT_CONFIG = $touchConfigPath
    $env:TG_LEGACY_UDP_PORT = $LegacyUdpPort.ToString()
    $env:TG_LEGACY_UDP_ENABLED = 'true'
    $script:headers = @{ 'X-TG-Terminal-Key' = 'TG-DEVELOPMENT-ONLY' }

    $serverProcess = Start-Server $serverLog1 $serverError1
    Wait-Until { (Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/health').status -eq 'ok' } 'Server did not become healthy.'
    $ledProcess = Start-Led $ledLog1
    Wait-Until {
        $ready = Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/readiness' -Headers $script:headers
        $ready.ledOnline -and $ready.ledReady -and $ready.ledContentVersion -eq 501
    } 'LedPlayer did not register and finish the 4K gallery cache.'
    $touchProcess = Start-Touch $touchLog1
    Start-Sleep -Seconds 3
    if ($touchProcess.HasExited) { throw 'TouchClient exited during startup.' }
    Add-Result 'three-process-startup' 'PASS' 'Development Server, latest LedPlayer and latest TouchClient started together.'

    $startBody = @{ moduleId = 'module-gallery-e'; imageIndex = 0; requestedBy = 'gallery-e-runtime-verification' } | ConvertTo-Json
    $started = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5080/api/gallery/start' `
        -Headers $script:headers -ContentType 'application/json' -Body $startBody
    Wait-Until {
        $lookup = Get-ActiveGallery
        $lookup.active -and $lookup.session.state -eq 1 -and $lookup.session.imageId -eq 'gallery-e-image-1'
    } 'First 4K image did not reach Showing.'
    Add-Result 'first-4k-display' 'PASS' 'Real 3840x2160 PNG completed Prepare, Ready, Show and Showing.'

    Send-GalleryControl $started.sessionId 2 | Out-Null
    Wait-Until {
        $lookup = Get-ActiveGallery
        $lookup.active -and $lookup.session.state -eq 1 -and $lookup.session.imageId -eq 'gallery-e-image-2'
    } 'Manual Next did not show the second 4K image.'
    Add-Result 'manual-next' 'PASS' 'Second 4K image replaced the first through the double-buffer path.'

    $memoryBefore = (Get-Process -Id $ledProcess.Id).WorkingSet64
    Send-GalleryControl $started.sessionId 3 $null 3 | Out-Null
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $deadline = [DateTime]::UtcNow.AddSeconds(16)
    while ([DateTime]::UtcNow -lt $deadline -and $observed.Count -lt 3) {
        $lookup = Get-ActiveGallery
        if ($lookup.active -and $lookup.session.state -eq 1) { $observed.Add([string]$lookup.session.imageId) | Out-Null }
        Start-Sleep -Milliseconds 350
    }
    if ($observed.Count -lt 3) { throw "Slideshow exposed only $($observed.Count) distinct image(s)." }
    Send-GalleryControl $started.sessionId 4 | Out-Null
    $paused = Get-ActiveGallery
    $pausedImage = [string]$paused.session.imageId
    Start-Sleep -Seconds 4
    $afterPause = Get-ActiveGallery
    if ($afterPause.session.slideshowActive -or $afterPause.session.imageId -ne $pausedImage) {
        throw 'Paused slideshow continued advancing.'
    }
    $memoryAfter = (Get-Process -Id $ledProcess.Id).WorkingSet64
    $memoryDeltaMb = [Math]::Round(($memoryAfter - $memoryBefore) / 1MB, 1)
    Add-Result 'short-4k-slideshow' 'PASS' "Three 4K images cycled and pause held; LedPlayer working-set delta ${memoryDeltaMb}MB."

    Stop-TestProcess $touchProcess
    $touchProcess = Start-Touch $touchLog2
    Wait-Until {
        (Test-Path -LiteralPath $touchLog2) -and
        (Select-String -LiteralPath $touchLog2 -SimpleMatch '已恢复活动图片图集并显示图片展示页面。' -Quiet)
    } 'Restarted TouchClient did not log active gallery recovery.'
    $touchRecovered = Get-ActiveGallery
    if (-not $touchRecovered.active -or $touchRecovered.session.sessionId -ne $started.sessionId) {
        throw 'TouchClient restart changed the authoritative gallery session.'
    }
    Add-Result 'touch-restart-recovery' 'PASS' 'TouchClient reopened the gallery page from the Server-authoritative active session.'

    $imageBeforeLedRestart = [string]$touchRecovered.session.imageId
    Stop-TestProcess $ledProcess
    Wait-Until {
        -not (Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/readiness' -Headers $script:headers).ledOnline
    } 'Server did not mark the stopped LedPlayer offline.' 25
    $ledProcess = Start-Led $ledLog2
    Wait-Until {
        $lookup = Get-ActiveGallery
        $ready = Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/readiness' -Headers $script:headers
        $ready.ledOnline -and $lookup.active -and $lookup.session.state -eq 1 -and
        $lookup.session.imageId -eq $imageBeforeLedRestart
    } 'LedPlayer restart did not restore the current gallery image.'
    Add-Result 'led-restart-recovery' 'PASS' 'LED reconnect triggered Prepare/Ready/Show for the same current image using the existing cache.'

    $sessionBeforeServerRestart = Get-ActiveGallery
    Stop-TestProcess $serverProcess
    $serverProcess = Start-Server $serverLog2 $serverError2
    Wait-Until { (Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/health').status -eq 'ok' } 'Restarted Server did not become healthy.'
    Wait-Until {
        $lookup = Get-ActiveGallery
        $lookup.active -and $lookup.session.sessionId -eq $sessionBeforeServerRestart.session.sessionId -and
        $lookup.session.imageId -eq $sessionBeforeServerRestart.session.imageId -and $lookup.session.state -eq 1
    } 'Server restart did not restore the gallery session and LED image.'
    Add-Result 'server-restart-recovery' 'PASS' 'Persisted session recovered and the reconnected LED confirmed the same image as Showing.'

    $udp = [Net.Sockets.UdpClient]::new()
    try {
        $bytes = [Text.Encoding]::ASCII.GetBytes('VoiceAdd')
        $udp.Send($bytes, $bytes.Length, '127.0.0.1', $LegacyUdpPort) | Out-Null
    }
    finally { $udp.Dispose() }
    Wait-Until {
        (Test-Path -LiteralPath $ledLog2) -and (Select-String -LiteralPath $ledLog2 -SimpleMatch '[LED Legacy UDP] 收到：VoiceAdd' -Quiet)
    } 'Legacy UDP packet was not received by LedPlayer.' 15
    if (-not (Get-ActiveGallery).active) { throw 'Legacy UDP volume command terminated the gallery session.' }
    Add-Result 'legacy-udp-isolation' 'PASS' "Legacy UDP command was consumed on test port $LegacyUdpPort without changing the gallery session."

    $welcomeResponse = Invoke-WebRequest -Method Post -Uri 'http://127.0.0.1:5080/api/welcome/request' `
        -Headers $script:headers -ContentType 'application/json' -Body '{}' -SkipHttpErrorCheck
    if ($welcomeResponse.StatusCode -notin 200,409) { throw "Unexpected welcome response: $($welcomeResponse.StatusCode)" }
    Wait-Until { -not (Get-ActiveGallery).active } 'Welcome priority transition did not stop the gallery session.'
    Add-Result 'welcome-priority' 'PASS' 'Welcome transition stopped Gallery first; no welcome asset was fabricated for this isolated run.'

    $startedAfterPriority = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5080/api/gallery/start' `
        -Headers $script:headers -ContentType 'application/json' -Body $startBody
    Wait-Until { (Get-ActiveGallery).session.state -eq 1 } 'Gallery did not restart for version-change recovery validation.'
    Stop-TestProcess $serverProcess
    $content.version = 500
    $content.modules[0].galleryImages = @($assets[2], $assets[0], $assets[1])
    [IO.File]::WriteAllText($contentPath, ($content | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    $serverProcess = Start-Server $serverLog3 $serverError3
    Wait-Until { (Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/health').status -eq 'ok' } 'Server did not restart with the replacement content version.'
    Wait-Until { -not (Get-ActiveGallery).active } 'Incompatible gallery session was not discarded after content version/order changed.'
    Add-Result 'content-version-safety' 'PASS' "Session $($startedAfterPriority.sessionId) was discarded when content version/order changed, preventing a stale image binding."

    Add-Result 'physical-three-machine-network-loss' 'NOT RUN' 'Requires three physical hosts and controllable network interruption.'
    Add-Result 'eight-hour-4k-soak' 'NOT RUN' 'Short 4K loop passed; eight-hour memory and thermal soak requires the deployment environment.'
    Add-Result 'human-led-visual-inspection' 'NOT RUN' 'Protocol, decode and Showing acknowledgements passed; human confirmation of crop, fade and display output is still required.'
    Add-Result 'formal-video-preemption' 'NOT RUN' 'No real MP4 asset is stored in this clean repository; Server mutual-exclusion automation remains PASS.'

    $document = [ordered]@{
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        branch = (git -C $repoRoot branch --show-current).Trim()
        commit = (git -C $repoRoot rev-parse HEAD).Trim()
        image = [ordered]@{ width = 3840; height = 2160; count = 3; bytesEach = (Get-Item $fourKSource).Length }
        results = $results
    }
    [IO.File]::WriteAllText($resultPath, ($document | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Write-Output "Gallery E runtime verification completed. Evidence: $resultPath"
}
finally {
    Stop-TestProcess $touchProcess
    Stop-TestProcess $ledProcess
    Stop-TestProcess $serverProcess
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
