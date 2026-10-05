[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackagesRoot)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PackagesRoot)

function Get-ExtendedPath([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    if ($full.StartsWith('\\')) { return '\\?\UNC\' + $full.Substring(2) }
    return '\\?\' + $full
}

function Get-Sha256Hex([string]$path) {
    $stream = [IO.File]::OpenRead((Get-ExtendedPath $path))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}

function Test-Package([string]$packageName, [string[]]$required) {
    $packageRoot = Join-Path $root $packageName
    $manifestPath = Join-Path $packageRoot 'package-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "$packageName manifest is missing." }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $packageRoot $_) -PathType Leaf) })
    if ($missing.Count -gt 0) { throw "$packageName required files are missing:`n$($missing -join "`n")" }
    if (Test-Path -LiteralPath (Join-Path $packageRoot 'Server\appsettings.Development.json') -PathType Leaf) {
        throw "$packageName contains development settings."
    }
    foreach ($entry in $manifest.files) {
        $path = Join-Path $packageRoot ([string]$entry.path).Replace('/', '\')
        $extended = Get-ExtendedPath $path
        if (-not [IO.File]::Exists($extended)) { throw "$packageName manifest file is missing: $($entry.path)" }
        $stream = [IO.File]::OpenRead($extended)
        try { $length = $stream.Length } finally { $stream.Dispose() }
        if ($length -ne [long]$entry.size) { throw "$packageName size mismatch: $($entry.path)" }
        if ((Get-Sha256Hex $path) -ne [string]$entry.sha256) { throw "$packageName SHA-256 mismatch: $($entry.path)" }
    }
}

Test-Package 'Server' @(
    'Server\TG.Control.Server.exe','Server\AdminWeb\index.html','TtsWorker\MeloTtsLocal\worker.py',
    'Tools\Install-TGServer.ps1','Tools\templates\server.site-install.json')
Test-Package 'Touch' @(
    'TouchClient\TouchClient.exe','Launcher\TG.Control.Launcher.exe',
    'Tools\Install-TGTerminal.ps1','Tools\templates\touch.site-install.json')
Test-Package 'Led' @(
    'LedPlayer\LedPlayer.exe','Launcher\TG.Control.Launcher.exe',
    'LedPlayer\LedPlayer_Data\Plugins\x86_64\AVProVideo.dll',
    'Tools\Install-TGTerminal.ps1','Tools\templates\led.site-install.json')

$forbiddenPatterns = @('F:\WorkSpace','C:\Users\A','TG-DEVELOPMENT-ONLY')
$textFiles = Get-ChildItem -LiteralPath $root -File -Recurse |
    Where-Object { $_.Extension -in @('.json','.config','.xml','.txt','.md','.ps1','.py') }
foreach ($file in $textFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    foreach ($pattern in $forbiddenPatterns) {
        if ($content -match [regex]::Escape($pattern)) { throw "Development value found in package: $($file.FullName)" }
    }
}

Write-Host 'PASS: Server, Touch and Led packages contain their isolated runtime trees and valid SHA-256 manifests.'
