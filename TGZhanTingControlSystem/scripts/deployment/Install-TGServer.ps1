[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [string]$DataRoot = (Join-Path $env:ProgramData 'TG Exhibition'),
    [string]$SiteConfig
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$serviceName = 'TG Exhibition Control Server'
$firewallRuleName = 'TG Exhibition Server API'
$install = [IO.Path]::GetFullPath($InstallRoot)
$data = [IO.Path]::GetFullPath($DataRoot)
$configDirectory = Join-Path $data 'Config'
$serverConfigPath = Join-Path $configDirectory 'server.site.json'
$credentialsPath = Join-Path $configDirectory 'initial-credentials.txt'
$enrollmentPath = Join-Path $configDirectory 'terminal-enrollment.txt'
$publicCertificatePath = Join-Path $configDirectory 'server-public.cer'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Installation must run from an elevated administrator process.'
    }
}

function New-RandomSecret([int]$byteCount) {
    $bytes = New-Object byte[] $byteCount
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return ([Convert]::ToBase64String($bytes).TrimEnd('=') -replace '\+', '-' -replace '/', '_')
}

function New-PasswordHash([string]$password) {
    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($salt) } finally { $rng.Dispose() }
    $derive = [Security.Cryptography.Rfc2898DeriveBytes]::new(
        $password, $salt, 210000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    try { $hash = $derive.GetBytes(32) } finally { $derive.Dispose() }
    return '$pbkdf2-sha256$210000${0}${1}' -f [Convert]::ToBase64String($salt),[Convert]::ToBase64String($hash)
}

function Protect-MachineSecret([string]$value) {
    $clear = [Text.Encoding]::UTF8.GetBytes($value)
    $encrypted = [System.Security.Cryptography.ProtectedData]::Protect(
        $clear, $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    return 'dpapi-local-machine:' + [Convert]::ToBase64String($encrypted)
}

function Unprotect-MachineSecret([string]$value) {
    $prefix = 'dpapi-local-machine:'
    if (-not $value.StartsWith($prefix, [StringComparison]::Ordinal)) { return $value }
    $encrypted = [Convert]::FromBase64String($value.Substring($prefix.Length))
    $clear = [System.Security.Cryptography.ProtectedData]::Unprotect(
        $encrypted, $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    return [Text.Encoding]::UTF8.GetString($clear)
}

function Write-Json([string]$path, [object]$value) {
    $json = $value | ConvertTo-Json -Depth 12
    $temporary = $path + '.tmp'
    [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $path -Force
}

function Set-ConfigProperty([object]$target, [string]$name, [object]$value) {
    if ($null -ne $target.PSObject.Properties[$name]) { $target.$name = $value }
    else { $target | Add-Member -NotePropertyName $name -NotePropertyValue $value }
}

function Get-CertificateFindSubject([string]$subject) {
    $trimmed = $subject.Trim()
    $commonNameMatch = [Text.RegularExpressions.Regex]::Match(
        $trimmed, '^CN\s*=\s*([^,]+)', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($commonNameMatch.Success) { return $commonNameMatch.Groups[1].Value.Trim() }
    return $trimmed
}

function Invoke-Sc([string[]]$arguments, [switch]$AllowFailure) {
    & "$env:SystemRoot\System32\sc.exe" @arguments | Out-Null
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        throw "sc.exe failed with exit code ${LASTEXITCODE}: $($arguments -join ' ')"
    }
}

function Ensure-HttpsCertificate([string]$subject, [string]$hostName) {
    $certificate = Get-ChildItem Cert:\LocalMachine\My | Where-Object {
        $certificateNames = @($_.DnsNameList | ForEach-Object { $_.Unicode })
        $_.Subject -eq $subject -and $_.NotAfter -gt [DateTime]::UtcNow.AddDays(30) -and
            $certificateNames -contains $hostName
    } | Sort-Object NotAfter -Descending | Select-Object -First 1
    if ($null -eq $certificate) {
        $ipAddress = $null
        if ([Net.IPAddress]::TryParse($hostName, [ref]$ipAddress)) {
            # Escape the literal braces because this string is also processed by PowerShell's -f operator.
            $san = '2.5.29.17={{text}}IPAddress={0}&DNS=localhost&DNS={1}' -f $hostName,$env:COMPUTERNAME
            $certificate = New-SelfSignedCertificate -Subject $subject -TextExtension @($san) `
                -CertStoreLocation 'Cert:\LocalMachine\My' -KeyAlgorithm RSA -KeyLength 3072 `
                -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter ([DateTime]::UtcNow.AddYears(5))
        } else {
            $dnsNames = @($hostName, 'localhost', $env:COMPUTERNAME) |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
            $certificate = New-SelfSignedCertificate -Subject $subject -DnsName $dnsNames `
                -CertStoreLocation 'Cert:\LocalMachine\My' -KeyAlgorithm RSA -KeyLength 3072 `
                -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter ([DateTime]::UtcNow.AddYears(5))
        }
    }
    $trusted = Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $certificate.Thumbprint
    if ($null -eq $trusted) {
        Export-Certificate -Cert $certificate -FilePath $publicCertificatePath -Force | Out-Null
        Import-Certificate -FilePath $publicCertificatePath -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
    } else {
        Export-Certificate -Cert $certificate -FilePath $publicCertificatePath -Force | Out-Null
    }
    return $certificate
}

Assert-Administrator
$serverExe = Join-Path $install 'Server\TG.Control.Server.exe'
if (-not (Test-Path -LiteralPath $serverExe -PathType Leaf)) { throw "Server executable is missing: $serverExe" }

$site = $null
if (-not [string]::IsNullOrWhiteSpace($SiteConfig) -and (Test-Path -LiteralPath $SiteConfig -PathType Leaf)) {
    # Windows PowerShell 5.1 otherwise decodes BOM-less UTF-8 as the active ANSI code page.
    $site = Get-Content -LiteralPath $SiteConfig -Raw -Encoding UTF8 | ConvertFrom-Json
}
$configuredBaseUrl = if ($null -ne $site -and -not [string]::IsNullOrWhiteSpace([string]$site.serverBaseUrl)) {
    ([string]$site.serverBaseUrl).TrimEnd('/')
} else {
    "https://$env:COMPUTERNAME`:5443"
}
$serverUri = $null
if ($configuredBaseUrl.Contains([char]0xFF1A)) {
    throw 'serverBaseUrl contains a full-width Chinese colon. Use an ASCII colon, for example https://192.168.86.40:5443.'
}
if (-not [Uri]::TryCreate($configuredBaseUrl, [UriKind]::Absolute, [ref]$serverUri) -or
    -not ($serverUri.Scheme -eq 'https' -or $serverUri.Scheme -eq 'http')) {
    throw 'serverBaseUrl must be an absolute HTTP or HTTPS address.'
}
$serverPort = $serverUri.Port
$serverHostName = $serverUri.Host
$certificateSubject = if ($null -ne $site -and -not [string]::IsNullOrWhiteSpace([string]$site.certificateSubject)) {
    [string]$site.certificateSubject
} else { 'CN=TG Exhibition Server' }
$certificateFindSubject = Get-CertificateFindSubject $certificateSubject

$directories = @('Config','Data','Media','Cache','Logs','Backups','Runtime')
foreach ($name in $directories) { New-Item -ItemType Directory -Path (Join-Path $data $name) -Force | Out-Null }
New-Item -ItemType Directory -Path (Join-Path $data 'Logs\Server') -Force | Out-Null

$existingConfigFiles = Get-ChildItem -LiteralPath $configDirectory -File -ErrorAction SilentlyContinue
if ($existingConfigFiles) {
    $backupDirectory = Join-Path $data ('Backups\preinstall-' + [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backupDirectory | Out-Null
    foreach ($file in $existingConfigFiles) { Copy-Item -LiteralPath $file.FullName -Destination $backupDirectory }
}

$firstInstall = -not (Test-Path -LiteralPath $serverConfigPath -PathType Leaf)
$requestedTerminalKey = if ($null -ne $site) { [string]$site.terminalApiKey } else { '' }
$terminalKey = if (-not [string]::IsNullOrWhiteSpace($requestedTerminalKey)) {
    $requestedTerminalKey
} elseif ($firstInstall) {
    New-RandomSecret 32
} else { '' }
$adminPassword = New-RandomSecret 18

if ($serverUri.Scheme -eq 'https') {
    $certificate = Ensure-HttpsCertificate $certificateSubject $serverHostName
}

if ($firstInstall) {
    $serverConfig = [ordered]@{
        Urls = "$($serverUri.Scheme)://0.0.0.0:$serverPort"
        Storage = @{ DataDirectory = (Join-Path $data 'Data') }
        Playback = @{
            TouchClientId = 'touch-main'; LedClientId = 'led-main'; PrepareLeadMilliseconds = 1500
            SyncToleranceMilliseconds = 500; LongPollSeconds = 20; RequireLedReadyBeforeStart = $true
            AllowDegradedPlayback = $true
        }
        Terminal = @{ ApiKey = (Protect-MachineSecret $terminalKey) }
        Admin = @{ AllowLegacyPlaintextPassword = $false; SessionHours = 12; Accounts = @(
            @{ Username = 'admin'; PasswordHash = (New-PasswordHash $adminPassword); Roles = @('Administrator') }
        ) }
        TtsProduction = @{
            EnableDeterministicTestProvider = $false; MaxTextLength = 5000; MaxAttempts = 3
            AttemptTimeoutMilliseconds = 300000; RetryDelayMilliseconds = 250
            MinAudioSizeBytes = 45; MaxAudioSizeBytes = 104857600
        }
        MeloTtsLocal = @{
            Enabled = $true; AutoStartWorker = $true; BaseAddress = 'http://127.0.0.1:5091'
            PythonExecutablePath = (Join-Path $install 'TtsWorker\MeloTtsLocal\runtime\python.exe')
            WorkerScriptPath = (Join-Path $install 'TtsWorker\MeloTtsLocal\worker.py')
            MeloTtsSourcePath = (Join-Path $install 'TtsWorker\MeloTtsLocal\vendor\MeloTTS')
            AcousticModelPath = (Join-Path $install 'TtsWorker\MeloTtsLocal\models\MeloTTS-Chinese')
            BertModelPath = (Join-Path $install 'TtsWorker\MeloTtsLocal\models\bert-base-multilingual-uncased')
            NltkDataPath = (Join-Path $install 'TtsWorker\MeloTtsLocal\runtime\nltk_data')
            HealthTimeoutMilliseconds = 2500; RestartDelayMilliseconds = 5000
        }
        Logging = @{ FileDirectory = (Join-Path $data 'Logs\Server'); LogLevel = @{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning' } }
    }
    if ($serverUri.Scheme -eq 'https') {
        $serverConfig.Kestrel = @{ Certificates = @{ Default = @{
            Subject = $certificateFindSubject; Store = 'My'; Location = 'LocalMachine'; AllowInvalid = $true
        } } }
    }
    Write-Json $serverConfigPath $serverConfig
} else {
    $effectiveConfig = Get-Content -LiteralPath $serverConfigPath -Raw | ConvertFrom-Json
    Set-ConfigProperty $effectiveConfig 'Urls' "$($serverUri.Scheme)://0.0.0.0:$serverPort"
    if ($serverUri.Scheme -eq 'https') {
        Set-ConfigProperty $effectiveConfig 'Kestrel' ([pscustomobject]@{ Certificates = [pscustomobject]@{ Default = [pscustomobject]@{
            Subject = $certificateFindSubject; Store = 'My'; Location = 'LocalMachine'; AllowInvalid = $true
        } } })
    }
    if (-not [string]::IsNullOrWhiteSpace($terminalKey)) {
        Set-ConfigProperty $effectiveConfig.Terminal 'ApiKey' (Protect-MachineSecret $terminalKey)
    } else {
        $terminalKey = Unprotect-MachineSecret ([string]$effectiveConfig.Terminal.ApiKey)
    }
    Write-Json $serverConfigPath $effectiveConfig
}

if ($firstInstall) {
    $credentialText = "管理地址：$configuredBaseUrl/`r`n管理员账号：admin`r`n初始密码：$adminPassword`r`n"
    [IO.File]::WriteAllText($credentialsPath, $credentialText, [Text.UTF8Encoding]::new($false))
}
$enrollmentText = "serverBaseUrl=$configuredBaseUrl`r`nterminalApiKey=$terminalKey`r`n"
[IO.File]::WriteAllText($enrollmentPath, $enrollmentText, [Text.UTF8Encoding]::new($false))

& "$env:SystemRoot\System32\icacls.exe" $configDirectory '/inheritance:r' '/grant:r' `
    'SYSTEM:(OI)(CI)F' 'Administrators:(OI)(CI)F' | Out-Null
foreach ($restrictedPath in @($serverConfigPath,$credentialsPath,$enrollmentPath,$publicCertificatePath)) {
    if (Test-Path -LiteralPath $restrictedPath) {
        & "$env:SystemRoot\System32\icacls.exe" $restrictedPath '/inheritance:r' '/grant:r' `
            'SYSTEM:F' 'Administrators:F' | Out-Null
    }
}
foreach ($writableName in @('Cache','Logs','Runtime')) {
    & "$env:SystemRoot\System32\icacls.exe" (Join-Path $data $writableName) '/inheritance:r' '/grant:r' `
        'SYSTEM:(OI)(CI)F' 'Administrators:(OI)(CI)F' 'Users:(OI)(CI)M' | Out-Null
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    Invoke-Sc @('delete', $serviceName)
    for ($attempt = 0; $attempt -lt 30 -and (Get-Service -Name $serviceName -ErrorAction SilentlyContinue); $attempt++) {
        Start-Sleep -Milliseconds 500
    }
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        throw 'The previous Server service could not be removed before registration.'
    }
}
New-Service -Name $serviceName -BinaryPathName ('"' + $serverExe + '"') `
    -DisplayName 'TG Exhibition Control Server' `
    -Description 'TG Exhibition content, playback coordination, and local TTS service' `
    -StartupType Automatic | Out-Null
Invoke-Sc @('failure', $serviceName, 'reset=', '86400', 'actions=', 'restart/5000/restart/10000/restart/30000')
Invoke-Sc @('failureflag', $serviceName, '1')

& "$env:SystemRoot\System32\netsh.exe" advfirewall firewall delete rule name="$firewallRuleName" | Out-Null
& "$env:SystemRoot\System32\netsh.exe" advfirewall firewall add rule name="$firewallRuleName" `
    dir=in action=allow protocol=TCP localport=$serverPort profile=private program="$serverExe" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not create Windows Firewall rule (exit $LASTEXITCODE)." }

Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
Write-Host "TG Exhibition Server installation completed. Management URL: $configuredBaseUrl/"
Write-Host "Terminal enrollment file: $enrollmentPath"
if ($serverUri.Scheme -eq 'https') { Write-Host "Public certificate: $publicCertificatePath" }
