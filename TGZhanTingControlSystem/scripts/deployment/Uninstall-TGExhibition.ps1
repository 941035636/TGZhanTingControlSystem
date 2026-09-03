[CmdletBinding()]
param(
    [string]$DataRoot = (Join-Path $env:ProgramData 'TG Exhibition'),
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$serviceName = 'TG Exhibition Control Server'
$firewallRuleName = 'TG Exhibition Server API'
$runValueName = 'TG Exhibition Launcher'

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        try { $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) } catch { }
    }
    & "$env:SystemRoot\System32\sc.exe" delete "$serviceName" | Out-Null
}
& "$env:SystemRoot\System32\netsh.exe" advfirewall firewall delete rule name="$firewallRuleName" | Out-Null
Remove-ItemProperty -Path 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $runValueName -ErrorAction SilentlyContinue

if ($RemoveData) {
    $resolvedData = [IO.Path]::GetFullPath($DataRoot).TrimEnd('\')
    $expectedData = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'TG Exhibition')).TrimEnd('\')
    if (-not [string]::Equals($resolvedData, $expectedData, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected data root: $resolvedData"
    }
    if (Test-Path -LiteralPath $resolvedData) { Remove-Item -LiteralPath $resolvedData -Recurse -Force }
}
