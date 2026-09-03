[CmdletBinding()]
param(
    [string]$ServerConfig = (Join-Path $env:ProgramData 'TG Exhibition\Config\server.site.json'),
    [string]$ServerBaseUrl = 'http://127.0.0.1:5080'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ServerConfig -PathType Leaf)) {
    throw "Server site configuration is missing: $ServerConfig"
}

$config = Get-Content -LiteralPath $ServerConfig -Raw | ConvertFrom-Json
$username = [string]$config.Admin.Username
$password = [string]$config.Admin.Password
if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password)) {
    throw 'Admin credentials are missing from the Server site configuration.'
}

$body = @{ username = $username; password = $password } | ConvertTo-Json
$response = Invoke-WebRequest -UseBasicParsing -Uri ($ServerBaseUrl.TrimEnd('/') + '/api/auth/login') `
    -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 10
if ($response.StatusCode -ne 200) { throw "Admin login returned HTTP $($response.StatusCode)." }

Write-Host 'PASS: Admin login API accepted the installed site credential.'
