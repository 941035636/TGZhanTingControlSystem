[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Touch','Led')][string]$Component,
    [string]$DataRoot = (Join-Path $env:ProgramData 'TG Exhibition'),
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$runValueName = if ($Component -eq 'Touch') { 'TG Exhibition Touch Launcher' } else { 'TG Exhibition Led Launcher' }
Remove-ItemProperty -Path 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $runValueName -ErrorAction SilentlyContinue

if ($RemoveData) {
    $resolvedData = [IO.Path]::GetFullPath($DataRoot).TrimEnd('\')
    $expectedData = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'TG Exhibition')).TrimEnd('\')
    if (-not [string]::Equals($resolvedData, $expectedData, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected data root: $resolvedData"
    }
    if (Test-Path -LiteralPath $resolvedData) { Remove-Item -LiteralPath $resolvedData -Recurse -Force }
}
