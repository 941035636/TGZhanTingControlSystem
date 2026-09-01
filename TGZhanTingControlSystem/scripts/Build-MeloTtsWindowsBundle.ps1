[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationRoot,
    [string]$DownloadCache = (Join-Path $env:LOCALAPPDATA 'TGControlSystem\MeloTtsBuildCache')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$pythonVersion = '3.10.11'
$meloVersion = '0.1.2'
$meloCommit = 'b633f243412169b999526e19eb6fcac0974b5d30'
$meloRevision = '082ca057e44f1e52ec47e1622a30286019e8a3ef'
$bertRevision = '7cbf9a625e29989f6b9c6c2fa68234c304f7e38f'
$scriptRoot = Split-Path -Parent $PSScriptRoot
$workerSource = Join-Path $scriptRoot 'src\TtsWorker\MeloTtsLocal'
$destination = [IO.Path]::GetFullPath($DestinationRoot)
$cache = [IO.Path]::GetFullPath($DownloadCache)

if (Test-Path -LiteralPath $destination) {
    if ((Get-ChildItem -LiteralPath $destination -Force | Select-Object -First 1)) {
        throw "DestinationRoot must be absent or empty: $destination"
    }
} else {
    New-Item -ItemType Directory -Path $destination | Out-Null
}
New-Item -ItemType Directory -Path $cache -Force | Out-Null

function Get-VerifiedFile {
    param([string]$Name, [string]$Uri, [string]$Sha256)
    $target = Join-Path $cache $Name
    if (-not (Test-Path -LiteralPath $target)) {
        Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $target
    }
    $actual = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Sha256.ToLowerInvariant()) {
        throw "SHA-256 mismatch for $Name. Expected $Sha256, got $actual."
    }
    return $target
}

function Get-HuggingFaceFile {
    param([string]$Repository, [string]$Revision, [string]$Name, [string]$Sha256)
    $safeName = ($Repository + '-' + $Revision + '-' + $Name) -replace '[/\\]', '-'
    return Get-VerifiedFile $safeName "https://huggingface.co/$Repository/resolve/$Revision/$Name?download=true" $Sha256
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('TG-MeloBundle-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $pythonArchive = Get-VerifiedFile "python-$pythonVersion-embed-amd64.zip" `
        "https://www.python.org/ftp/python/$pythonVersion/python-$pythonVersion-embed-amd64.zip" `
        '608619f8619075629c9c69f361352a0da6ed7e62f83a0e19c63e0ea32eb7629d'
    $meloArchive = Get-VerifiedFile "MeloTTS-v$meloVersion.zip" `
        "https://github.com/myshell-ai/MeloTTS/archive/refs/tags/v$meloVersion.zip" `
        '33acba732ce6689f38f140bf0a310227320f4c522fa9ed99744f21e8ca4c17d1'
    $getPip = Get-VerifiedFile 'get-pip.py' 'https://bootstrap.pypa.io/get-pip.py' `
        'fb24e693bab954209a063d90953621412ccad4a500905a726286e038f508ddf6'

    $runtime = Join-Path $destination 'runtime'
    Expand-Archive -LiteralPath $pythonArchive -DestinationPath $runtime
    $pth = Join-Path $runtime 'python310._pth'
    $pthLines = [Collections.Generic.List[string]](Get-Content -LiteralPath $pth)
    for ($index = 0; $index -lt $pthLines.Count; $index++) {
        if ($pthLines[$index] -eq '#import site') { $pthLines[$index] = 'import site' }
    }
    [IO.File]::WriteAllLines($pth, $pthLines, [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $getPip -Destination (Join-Path $runtime 'get-pip.py')
    & (Join-Path $runtime 'python.exe') (Join-Path $runtime 'get-pip.py') --disable-pip-version-check
    if ($LASTEXITCODE -ne 0) { throw 'get-pip failed.' }
    & (Join-Path $runtime 'python.exe') -m pip install --disable-pip-version-check --no-warn-script-location `
        -r (Join-Path $workerSource 'requirements-windows-cpu.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Python dependency installation failed.' }

    $meloExtract = Join-Path $temporary 'melo'
    Expand-Archive -LiteralPath $meloArchive -DestinationPath $meloExtract
    $meloSource = Get-ChildItem -LiteralPath $meloExtract -Directory | Select-Object -First 1
    if ($null -eq $meloSource) { throw 'MeloTTS archive has no source directory.' }
    $vendorRoot = Join-Path $destination 'vendor'
    New-Item -ItemType Directory -Path $vendorRoot | Out-Null
    Copy-Item -LiteralPath $meloSource.FullName -Destination (Join-Path $vendorRoot 'MeloTTS') -Recurse
    $patch = Join-Path $workerSource 'melotts-v0.1.2-windows-offline.patch'
    & git -C (Join-Path $vendorRoot 'MeloTTS') apply --check $patch
    if ($LASTEXITCODE -ne 0) { throw 'MeloTTS Windows/offline patch check failed.' }
    & git -C (Join-Path $vendorRoot 'MeloTTS') apply $patch
    if ($LASTEXITCODE -ne 0) { throw 'MeloTTS Windows/offline patch failed.' }

    $acoustic = Join-Path $destination 'models\MeloTTS-Chinese'
    $bert = Join-Path $destination 'models\bert-base-multilingual-uncased'
    New-Item -ItemType Directory -Path $acoustic,$bert | Out-Null
    Copy-Item (Get-HuggingFaceFile 'myshell-ai/MeloTTS-Chinese' $meloRevision 'config.json' `
        'd58b5acdab89ad2bbd65325affab309ae3cb964834b02f9a60587474e81c8bb9') (Join-Path $acoustic 'config.json')
    Copy-Item (Get-HuggingFaceFile 'myshell-ai/MeloTTS-Chinese' $meloRevision 'checkpoint.pth' `
        'a74e9eadffff065c75eb6dfa040efa72cad23e72cfea70d39190bc174fb97093') (Join-Path $acoustic 'checkpoint.pth')
    Copy-Item (Get-HuggingFaceFile 'bert-base-multilingual-uncased' $bertRevision 'config.json' `
        'fba5d4b0a351a43f6ccb7a6587301fd9f6876ca36aae62af762af67c8f18db1c') (Join-Path $bert 'config.json')
    Copy-Item (Get-HuggingFaceFile 'bert-base-multilingual-uncased' $bertRevision 'pytorch_model.bin' `
        '2fec0e2a13cde5fa386fa00ba3e1bfea14b5d8fd8760f37f051799812a320e8d') (Join-Path $bert 'pytorch_model.bin')
    Copy-Item (Get-HuggingFaceFile 'bert-base-multilingual-uncased' $bertRevision 'vocab.txt' `
        '87b44292b452f6c05afa49b2e488e7eedf79ea4f4c39db6f2f4b37764228ef3f') (Join-Path $bert 'vocab.txt')

    $nltkRoot = Join-Path $runtime 'nltk_data'
    New-Item -ItemType Directory -Path (Join-Path $nltkRoot 'corpora'),(Join-Path $nltkRoot 'taggers') | Out-Null
    $cmudict = Get-VerifiedFile 'nltk-cmudict.zip' `
        'https://raw.githubusercontent.com/nltk/nltk_data/gh-pages/packages/corpora/cmudict.zip' `
        'd07cca47fd72ad32ea9d8ad1219f85301eeaf4568f8b6b73747506a71fb5afd6'
    $tagger = Get-VerifiedFile 'nltk-averaged_perceptron_tagger.zip' `
        'https://raw.githubusercontent.com/nltk/nltk_data/gh-pages/packages/taggers/averaged_perceptron_tagger.zip' `
        'e1f13cf2532daadfd6f3bc481a49859f0b8ea6432ccdcd83e6a49a5f19008de9'
    Expand-Archive -LiteralPath $cmudict -DestinationPath (Join-Path $nltkRoot 'corpora')
    Expand-Archive -LiteralPath $tagger -DestinationPath (Join-Path $nltkRoot 'taggers')

    Copy-Item -LiteralPath (Join-Path $workerSource 'worker.py') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $workerSource 'melotts-v0.1.2-windows-offline.patch') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $workerSource 'requirements-windows-cpu.txt') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $workerSource 'README.md') -Destination $destination

    $manifest = [ordered]@{
        schemaVersion = 1
        builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        pythonVersion = $pythonVersion
        meloTtsVersion = $meloVersion
        meloTtsCommit = $meloCommit
        acousticModelRevision = $meloRevision
        bertModelRevision = $bertRevision
        providerId = 'melo-local'
        voiceIds = @('zh-standard')
        offlineReady = $true
    }
    [IO.File]::WriteAllText((Join-Path $destination 'bundle-manifest.json'),
        ($manifest | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
    Write-Host "MeloTTS offline bundle created: $destination"
} finally {
    if (Test-Path -LiteralPath $temporary) {
        $resolvedTemporary = (Resolve-Path -LiteralPath $temporary).Path
        $resolvedTempRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path
        if ($resolvedTemporary.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
        }
    }
}
