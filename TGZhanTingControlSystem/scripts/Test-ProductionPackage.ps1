[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackageRoot)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PackageRoot)

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
$manifestPath = Join-Path $root 'package-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'package-manifest.json is missing.' }
try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
} catch {
    throw "package-manifest.json is not valid JSON: $($_.Exception.Message)"
}

$required = @(
    'Server\TG.Control.Server.exe', 'Server\AdminWeb\index.html',
    'TouchClient\TouchClient.exe', 'TouchClient\TouchClient_Data\globalgamemanagers',
    'LedPlayer\LedPlayer.exe', 'LedPlayer\LedPlayer_Data\globalgamemanagers',
    'LedPlayer\LedPlayer_Data\Plugins\libvlc.dll',
    'Launcher\TG.Control.Launcher.exe',
    'TtsWorker\MeloTtsLocal\runtime\python.exe',
    'TtsWorker\MeloTtsLocal\worker.py',
    'TtsWorker\MeloTtsLocal\models\MeloTTS-Chinese\config.json',
    'TtsWorker\MeloTtsLocal\models\MeloTTS-Chinese\checkpoint.pth',
    'TtsWorker\MeloTtsLocal\models\bert-base-multilingual-uncased\config.json',
    'TtsWorker\MeloTtsLocal\models\bert-base-multilingual-uncased\pytorch_model.bin',
    'TtsWorker\MeloTtsLocal\models\bert-base-multilingual-uncased\vocab.txt',
    'Tools\Install-TGExhibition.ps1', 'Tools\Uninstall-TGExhibition.ps1',
    'ThirdParty\NOTICE.md', 'ThirdParty\MeloTTS-LICENSE.txt', 'ThirdParty\InnoSetup-LICENSE.txt'
)
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf) })
if ($missing.Count -gt 0) { throw "Package required files are missing:`n$($missing -join "`n")" }

$mismatches = [Collections.Generic.List[string]]::new()
foreach ($entry in $manifest.files) {
    $path = Join-Path $root ([string]$entry.path).Replace('/', '\')
    $extended = Get-ExtendedPath $path
    if (-not [IO.File]::Exists($extended)) { $mismatches.Add("missing: $($entry.path)"); continue }
    $stream = [IO.File]::OpenRead($extended)
    try { $length = $stream.Length } finally { $stream.Dispose() }
    if ($length -ne [long]$entry.size) { $mismatches.Add("size: $($entry.path)"); continue }
    $sha = Get-Sha256Hex $path
    if ($sha -ne [string]$entry.sha256) { $mismatches.Add("sha256: $($entry.path)") }
}
if ($mismatches.Count -gt 0) { throw "Package integrity validation failed:`n$($mismatches -join "`n")" }

$forbiddenPatterns = @('F:\WorkSpace', 'C:\Users\A', 'AppData\Local\Temp\TG-Phase9E')
$textFiles = Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object { $_.Extension -in @('.json','.config','.xml','.txt','.md','.ps1','.py') }
foreach ($file in $textFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    foreach ($pattern in $forbiddenPatterns) {
        if ($content -match [regex]::Escape($pattern)) { throw "Development absolute path found in package: $($file.FullName)" }
    }
}

Write-Host "PASS: package contains all runtime trees and $($manifest.fileCount) manifest-tracked files with valid SHA-256."
