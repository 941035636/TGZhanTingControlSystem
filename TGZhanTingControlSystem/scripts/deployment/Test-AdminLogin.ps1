[CmdletBinding()]
param(
    [string]$ServerConfig = (Join-Path $env:ProgramData 'TG Exhibition\Config\server.site.json'),
    [string]$CredentialsPath = (Join-Path $env:ProgramData 'TG Exhibition\Config\initial-credentials.txt'),
    [string]$ServerBaseUrl = 'https://localhost:5443',
    [string]$Password
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ServerConfig -PathType Leaf)) {
    throw "Server site configuration is missing: $ServerConfig"
}

$config = Get-Content -LiteralPath $ServerConfig -Raw | ConvertFrom-Json
$username = if ($config.Admin.Accounts.Count -gt 0) { [string]$config.Admin.Accounts[0].Username } else { [string]$config.Admin.Username }
if ([string]::IsNullOrWhiteSpace($Password) -and (Test-Path -LiteralPath $CredentialsPath -PathType Leaf)) {
    $credentialText = Get-Content -LiteralPath $CredentialsPath -Raw
    if ($credentialText -match '(?m)^Initial password:\s*(.+?)\s*$') { $Password = $Matches[1] }
}
$password = $Password
if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password)) {
    throw 'Admin login test needs -Password or the ACL-restricted initial-credentials.txt file.'
}

$body = @{ username = $username; password = $password } | ConvertTo-Json
$response = Invoke-WebRequest -UseBasicParsing -Uri ($ServerBaseUrl.TrimEnd('/') + '/api/auth/login') `
    -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 10
if ($response.StatusCode -ne 200) { throw "Admin login returned HTTP $($response.StatusCode)." }

Write-Host 'PASS: Admin login API accepted the installed site credential.'
