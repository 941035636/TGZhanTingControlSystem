param(
    [Parameter(Mandatory)][string]$ServerUrl,
    [Parameter(Mandatory)][int]$PlayerId,
    [Parameter(Mandatory)][string]$PlayerLogPath,
    [Parameter(Mandatory)][string]$ModuleId,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][switch]$IsolatedTestEnvironment,
    [string]$TerminalKey = 'TG-DEVELOPMENT-ONLY'
)

# Requires an actual Windows LED Player and an isolated Server containing a route
# with at least two bright, moving test videos (>= 12 seconds each). The window
# is shown for rendering/capture. This is not an API-only or headless video test.
$ErrorActionPreference = 'Stop'
if (-not $IsolatedTestEnvironment) { throw 'Use an isolated test Server/data directory.' }
$serverUri = [Uri]$ServerUrl
if (-not $serverUri.IsLoopback -or $serverUri.Port -eq 5080) {
    throw 'This regression is restricted to a separate loopback test Server, not the default live port 5080.'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$headers = @{ 'X-TG-Terminal-Key' = $TerminalKey }
$results = [System.Collections.Generic.List[object]]::new()
$sessionId = $null
$completed = $false

function Request([string]$Path, $Body = $null) {
    $options = @{ Uri = "$($ServerUrl.TrimEnd('/'))$Path"; Headers = $headers; TimeoutSec = 10 }
    if ($null -ne $Body) { $options.Method = 'Post'; $options.ContentType = 'application/json'; $options.Body = $Body | ConvertTo-Json -Depth 10 }
    Invoke-RestMethod @options
}
function Check([string]$Name, [bool]$Condition) {
    $results.Add([pscustomobject]@{ test = $Name; status = $(if ($Condition) { 'PASS' } else { 'FAIL' }); utc = [DateTimeOffset]::UtcNow.ToString('O') })
    if (-not $Condition) { throw $Name }
    Write-Host "PASS: $Name"
}
function Control([int]$Action) {
    $response = Request '/api/playback/control' @{ sessionId = $sessionId; action = $Action }
    if (-not $response.accepted) { throw "Control $Action was rejected." }
}
function Wait-Until([scriptblock]$Condition, [int]$Seconds = 20) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Timed out waiting for Player/Session state.'
}

Add-Type -AssemblyName System.Drawing
if (-not ('TgLedVideoCapture' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TgLedVideoCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool Callback(IntPtr hwnd, IntPtr arg);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr arg);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int size);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    public static IntPtr Find(int pid) {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hwnd, arg) => {
            uint owner; GetWindowThreadProcessId(hwnd, out owner);
            var name = new System.Text.StringBuilder(256); GetClassName(hwnd, name, 256);
            if (owner == pid && name.ToString() == "UnityWndClass") { result = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@
}

$handle = [TgLedVideoCapture]::Find($PlayerId)
if ($handle -eq [IntPtr]::Zero) { throw 'The specified process has no Unity Player window.' }
function Capture([string]$Name) {
    $rect = New-Object TgLedVideoCapture+Rect
    [TgLedVideoCapture]::GetClientRect($handle, [ref]$rect) | Out-Null
    $bitmap = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        try { if (-not [TgLedVideoCapture]::PrintWindow($handle, $dc, 3)) { throw 'Window capture failed.' } }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        $bitmap.Save((Join-Path $OutputDirectory "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        # Ignore the status overlay; compare sampled RGB pixels in the video region.
        $pixels = [System.Collections.Generic.List[int]]::new()
        $bright = 0
        for ($y = [int]($bitmap.Height * 0.2); $y -lt $bitmap.Height * 0.9; $y += 11) {
            for ($x = [int]($bitmap.Width * 0.1); $x -lt $bitmap.Width * 0.9; $x += 11) {
                $color = $bitmap.GetPixel($x, $y)
                $pixels.Add($color.ToArgb())
                if ([Math]::Max($color.R, [Math]::Max($color.G, $color.B)) -gt 40) { $bright++ }
            }
        }
        return @{ signature = ($pixels -join ','); brightRatio = $bright / [double]$pixels.Count }
    } finally { $bitmap.Dispose() }
}

try {
    Check 'No existing active Session is modified' (-not (Request '/api/playback/active').active)
    [TgLedVideoCapture]::ShowWindow($handle, 5) | Out-Null
    [TgLedVideoCapture]::SetForegroundWindow($handle) | Out-Null
    Wait-Until { (Request '/api/readiness').ledReady } 40
    $started = Request '/api/playback/start' @{ moduleIds = @($ModuleId); requestedBy = 'LED D3D regression' }
    $sessionId = $started.sessionId
    Check 'Route has multiple nodes' ($started.nodeCount -ge 2)
    Wait-Until { (Request '/api/playback/active').session.playPublished }
    Start-Sleep -Seconds 3
    $playing = Capture '01-playing'
    Check 'Actual video region is not black' ($playing.brightRatio -gt 0.1)

    Control 3
    Wait-Until { (Request '/api/playback/active').session.paused }
    Start-Sleep -Seconds 1
    $paused = Capture '02-paused'
    Start-Sleep -Seconds 1
    $pausedAgain = Capture '03-still-paused'
    Check 'Pause freezes the actual video pixels' ($paused.signature -eq $pausedAgain.signature)
    Control 4
    Wait-Until { -not (Request '/api/playback/active').session.paused }
    Start-Sleep -Seconds 2
    $resumed = Capture '04-resumed'
    Check 'Resume advances actual video pixels' ($resumed.signature -ne $pausedAgain.signature -and $resumed.brightRatio -gt 0.1)

    $beforeSkip = (Request '/api/playback/active').session.currentNodeNumber
    Control 7
    Wait-Until { (Request '/api/playback/active').session.currentNodeNumber -gt $beforeSkip }
    Wait-Until { (Request '/api/playback/active').session.playPublished }
    Start-Sleep -Seconds 3
    $skipped = Capture '05-next-node'
    Check 'Skip renders the next video' ($skipped.brightRatio -gt 0.1)
    Control 8
    Start-Sleep -Seconds 4
    $retried = Capture '06-retry'
    Check 'Retry renders video again' ($retried.brightRatio -gt 0.1)
    Wait-Until { -not (Request '/api/playback/active').active } 60
    Check 'Multi-node playback completes' $true

    $log = Get-Content -LiteralPath $PlayerLogPath -Raw
    Check 'No unsupported D3D format or playback exception' ($log -notmatch 'Unsupported D3D|DllNotFoundException|EntryPointNotFoundException|NullReferenceException')
    Check 'Unity BGRA32 output confirmed by the active backend' ($log -match 'Video output verified via LibVLC / UMP \(Unity BGRA32\)')
    $completed = $true
} catch {
    $results.Add([pscustomobject]@{ test = 'Regression execution'; status = 'FAIL'; detail = $_.Exception.Message; utc = [DateTimeOffset]::UtcNow.ToString('O') })
    throw
} finally {
    try {
        if ($sessionId) {
            $active = Request '/api/playback/active'
            if ($active.active -and $active.session.sessionId -eq $sessionId) { Control 5 }
        }
    } catch {
        $completed = $false
        $results.Add([pscustomobject]@{ test = 'Stop owned test Session'; status = 'FAIL'; detail = $_.Exception.Message })
        throw
    } finally {
        @{ status = $(if ($completed) { 'PASS' } else { 'FAIL' }); server = $ServerUrl; playerId = $PlayerId;
            playerLog = $PlayerLogPath; tests = @($results.ToArray()) } |
            ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json') -Encoding utf8
    }
}
