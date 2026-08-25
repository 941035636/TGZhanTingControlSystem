param(
    [string]$Output = "artifacts\server-win-x64"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build-All.ps1')
$destination = Join-Path $root $Output
dotnet publish (Join-Path $root 'src\Server\TG.Control.Server\TG.Control.Server.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $destination
Write-Host "Published server to $destination"
