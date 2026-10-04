[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9._-]{3,64}$')][string]$Username,
    [ValidateSet('Administrator','Publisher','Editor','Operator','Viewer')][string[]]$Roles = @('Viewer')
)

$ErrorActionPreference = 'Stop'
$first = Read-Host "Password for $Username" -AsSecureString
$second = Read-Host 'Repeat password' -AsSecureString

function Convert-SecureStringToText([Security.SecureString]$value) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($value)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$password = Convert-SecureStringToText $first
$confirmation = Convert-SecureStringToText $second
try {
    if ($password -cne $confirmation) { throw 'Passwords do not match.' }
    if ($password.Length -lt 14) { throw 'Production administrator passwords must contain at least 14 characters.' }
    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($salt) } finally { $rng.Dispose() }
    $derive = [Security.Cryptography.Rfc2898DeriveBytes]::new(
        $password, $salt, 210000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    try { $hash = $derive.GetBytes(32) } finally { $derive.Dispose() }
    $encoded = '$pbkdf2-sha256$210000${0}${1}' -f [Convert]::ToBase64String($salt),[Convert]::ToBase64String($hash)
    [ordered]@{ Username = $Username; PasswordHash = $encoded; Roles = @($Roles | Select-Object -Unique) } |
        ConvertTo-Json -Depth 4
} finally {
    $password = $null
    $confirmation = $null
    $first.Dispose()
    $second.Dispose()
}
