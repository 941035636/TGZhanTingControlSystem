[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [string]$ServiceName = 'TG Exhibition Control Server'
)

$ErrorActionPreference = 'Stop'
$install = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$serverRoot = (Join-Path $install 'Server').TrimEnd('\') + '\'
$workerRoot = (Join-Path $install 'TtsWorker').TrimEnd('\') + '\'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Server upgrade preparation must run from an elevated administrator process.'
    }
}

function Get-TGOwnedProcesses {
    @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $path = [string]$_.ExecutablePath
        -not [string]::IsNullOrWhiteSpace($path) -and
        ($path.StartsWith($serverRoot, [StringComparison]::OrdinalIgnoreCase) -or
         $path.StartsWith($workerRoot, [StringComparison]::OrdinalIgnoreCase))
    })
}

function Test-FileUnlocked([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $true }
    try {
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
            [IO.FileShare]::None)
        $stream.Dispose()
        return $true
    } catch [IO.IOException] {
        return $false
    } catch [UnauthorizedAccessException] {
        return $false
    }
}

Assert-Administrator

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $service -and $service.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    try { $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) } catch { }
}

# The Server normally terminates the worker tree itself. This targeted fallback handles an
# interrupted upgrade or an older build that left its embedded Python process orphaned.
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    $owned = Get-TGOwnedProcesses
    if ($owned.Count -eq 0) { break }
    foreach ($process in $owned) {
        Stop-Process -Id ([int]$process.ProcessId) -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 250
}

$remaining = Get-TGOwnedProcesses
if ($remaining.Count -gt 0) {
    throw 'The previous Server or MeloTTS worker process could not be stopped before upgrade.'
}

$lockProbe = Join-Path $workerRoot 'MeloTtsLocal\runtime\libcrypto-1_1.dll'
if (-not (Test-Path -LiteralPath $lockProbe -PathType Leaf)) { exit 0 }
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    if (Test-FileUnlocked $lockProbe) { exit 0 }
    Start-Sleep -Milliseconds 250
}

throw "The previous MeloTTS runtime is still locked: $lockProbe"
