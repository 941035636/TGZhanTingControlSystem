[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageRoot,
    [string]$Version = '1.0.0',
    [string]$GitCommit
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PackageRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Package root was not found: $root"
}

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
$manifestFiles = @(Get-ChildItem -LiteralPath $root -File -Recurse |
    Where-Object { $_.FullName -ne $manifestPath } |
    Sort-Object FullName |
    ForEach-Object {
        [pscustomobject][ordered]@{
            path = $_.FullName.Substring($root.Length + 1).Replace('\','/')
            size = $_.Length
            sha256 = Get-Sha256Hex $_.FullName
        }
    })

if ([string]::IsNullOrWhiteSpace($GitCommit)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $GitCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
}

$manifest = [ordered]@{
    schemaVersion = 1
    product = 'TG Exhibition Control System'
    version = $Version
    gitCommit = $GitCommit
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    target = 'Windows 10/11 x64'
    fileCount = $manifestFiles.Count
    totalSize = [long](($manifestFiles | ForEach-Object { $_.size } | Measure-Object -Sum).Sum)
    files = $manifestFiles
}

[IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 6),
    [Text.UTF8Encoding]::new($false))

Write-Host "Production package manifest written: $manifestPath"
Write-Host "Files: $($manifest.fileCount); bytes: $($manifest.totalSize)"
