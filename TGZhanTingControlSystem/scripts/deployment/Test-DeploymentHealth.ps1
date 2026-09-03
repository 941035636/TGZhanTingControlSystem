[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:ProgramFiles 'TG Exhibition'),
    [string]$DataRoot = (Join-Path $env:ProgramData 'TG Exhibition'),
    [string]$ServerHealthUrl = 'http://127.0.0.1:5080/api/health'
)

$ErrorActionPreference = 'Stop'
$required = @(
    (Join-Path $InstallRoot 'Server\TG.Control.Server.exe'),
    (Join-Path $InstallRoot 'TouchClient\TouchClient.exe'),
    (Join-Path $InstallRoot 'LedPlayer\LedPlayer.exe'),
    (Join-Path $InstallRoot 'Launcher\TG.Control.Launcher.exe'),
    (Join-Path $InstallRoot 'TtsWorker\MeloTtsLocal\runtime\python.exe'),
    (Join-Path $InstallRoot 'TtsWorker\MeloTtsLocal\models\MeloTTS-Chinese\checkpoint.pth'),
    (Join-Path $DataRoot 'Config\server.site.json')
)
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missing.Count -gt 0) { throw "Deployment files are missing:`n$($missing -join "`n")" }

$service = Get-Service -Name 'TG Exhibition Control Server' -ErrorAction Stop
if ($service.Status -ne 'Running') { throw "Server service is not running: $($service.Status)" }
$health = Invoke-RestMethod -Uri $ServerHealthUrl -TimeoutSec 10
if ($health.status -ne 'ok') { throw 'Server health endpoint returned an unexpected payload.' }
Write-Host 'PASS: deployment files, Windows Service and Server health endpoint are ready.'
