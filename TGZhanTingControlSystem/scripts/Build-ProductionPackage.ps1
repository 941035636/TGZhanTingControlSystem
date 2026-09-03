[CmdletBinding()]
param(
    [string]$OutputRoot = 'artifacts\Phase9G',
    [string]$Version = '1.0.0',
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2020.3.35f1c2\Editor\Unity.exe',
    [string]$ExistingUnityBuildRoot,
    [string]$MeloTtsBundleSource,
    [string]$InnoCompiler,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$packageRoot = Join-Path $artifactRoot 'Package'
$installerOutput = Join-Path $artifactRoot 'Installer'

function Reset-ArtifactDirectory([string]$path) {
    $resolvedRepoArtifacts = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath($path)
    if (-not $resolved.StartsWith($resolvedRepoArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside the repository artifacts root: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        $empty = Join-Path ([IO.Path]::GetTempPath()) ('TG-Empty-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $empty | Out-Null
        try {
            & "$env:SystemRoot\System32\robocopy.exe" $empty $resolved /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "Could not reset artifact directory (robocopy $LASTEXITCODE): $resolved" }
        } finally {
            Remove-Item -LiteralPath $empty -Force
        }
    } else {
        New-Item -ItemType Directory -Path $resolved | Out-Null
    }
}

function Copy-Directory([string]$source, [string]$destination) {
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw "Required directory is missing: $source" }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    & "$env:SystemRoot\System32\robocopy.exe" $source $destination /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Directory copy failed (robocopy $LASTEXITCODE): $source" }
}

function Invoke-UnityBuild([string]$projectPath, [string]$method, [string]$destination, [string]$logPath) {
    if (-not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) { throw "Unity Editor was not found: $UnityEditor" }
    $previousOutput = $env:TG_WINDOWS_BUILD_OUTPUT
    try {
        $env:TG_WINDOWS_BUILD_OUTPUT = $destination
        & $UnityEditor -batchmode -quit -nographics -projectPath $projectPath -executeMethod $method -logFile $logPath
        if ($LASTEXITCODE -ne 0) { throw "Unity build failed for $projectPath (exit $LASTEXITCODE). See $logPath" }
    } finally {
        $env:TG_WINDOWS_BUILD_OUTPUT = $previousOutput
    }
}

Reset-ArtifactDirectory $artifactRoot
New-Item -ItemType Directory -Path $packageRoot,$installerOutput | Out-Null

Write-Host 'Publishing self-contained Server and embedded AdminWeb...'
dotnet publish (Join-Path $repoRoot 'src\Server\TG.Control.Server\TG.Control.Server.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:DebugSymbols=false -p:DebugType=None `
    --output (Join-Path $packageRoot 'Server')
if ($LASTEXITCODE -ne 0) { throw 'Server production publish failed.' }

Write-Host 'Publishing self-contained Runtime Launcher...'
dotnet publish (Join-Path $repoRoot 'src\Launcher\TG.Control.Launcher\TG.Control.Launcher.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugSymbols=false -p:DebugType=None `
    --output (Join-Path $packageRoot 'Launcher')
if ($LASTEXITCODE -ne 0) { throw 'Runtime Launcher production publish failed.' }

if ([string]::IsNullOrWhiteSpace($ExistingUnityBuildRoot)) {
    $unityOutput = Join-Path $artifactRoot 'UnityBuilds'
    New-Item -ItemType Directory -Path $unityOutput | Out-Null
    Invoke-UnityBuild (Join-Path $repoRoot 'src\TouchClient') 'TG.Control.Editor.WindowsPlayerBuilder.Build' `
        (Join-Path $unityOutput 'TouchClient\TouchClient.exe') (Join-Path $artifactRoot 'TouchClient-build.log')
    Invoke-UnityBuild (Join-Path $repoRoot 'src\LedPlayer') 'TG.Control.Editor.WindowsPlayerBuilder.Build' `
        (Join-Path $unityOutput 'LedPlayer\LedPlayer.exe') (Join-Path $artifactRoot 'LedPlayer-build.log')
} else {
    $unityOutput = [IO.Path]::GetFullPath($ExistingUnityBuildRoot)
}
Copy-Directory (Join-Path $unityOutput 'TouchClient') (Join-Path $packageRoot 'TouchClient')
Copy-Directory (Join-Path $unityOutput 'LedPlayer') (Join-Path $packageRoot 'LedPlayer')

if ([string]::IsNullOrWhiteSpace($MeloTtsBundleSource)) {
    $meloDestination = Join-Path $packageRoot 'TtsWorker\MeloTtsLocal'
    & (Join-Path $repoRoot 'scripts\Build-MeloTtsWindowsBundle.ps1') -DestinationRoot $meloDestination
    if ($LASTEXITCODE -ne 0) { throw 'MeloTTS offline bundle build failed.' }
} else {
    Copy-Directory ([IO.Path]::GetFullPath($MeloTtsBundleSource)) (Join-Path $packageRoot 'TtsWorker\MeloTtsLocal')
}

Copy-Directory (Join-Path $repoRoot 'scripts\deployment') (Join-Path $packageRoot 'Tools')
Copy-Directory (Join-Path $repoRoot 'ThirdParty') (Join-Path $packageRoot 'ThirdParty')

$requiredFiles = @(
    'Server\TG.Control.Server.exe', 'Server\AdminWeb\index.html',
    'TouchClient\TouchClient.exe', 'LedPlayer\LedPlayer.exe',
    'Launcher\TG.Control.Launcher.exe', 'TtsWorker\MeloTtsLocal\worker.py',
    'TtsWorker\MeloTtsLocal\runtime\python.exe',
    'TtsWorker\MeloTtsLocal\models\MeloTTS-Chinese\checkpoint.pth',
    'TtsWorker\MeloTtsLocal\models\bert-base-multilingual-uncased\pytorch_model.bin',
    'ThirdParty\NOTICE.md', 'ThirdParty\MeloTTS-LICENSE.txt', 'ThirdParty\InnoSetup-LICENSE.txt',
    'Tools\Install-TGExhibition.ps1', 'Tools\Test-AdminLogin.ps1'
)
$missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $packageRoot $_) -PathType Leaf) })
if ($missingFiles.Count -gt 0) { throw "Production package is incomplete:`n$($missingFiles -join "`n")" }

Write-Host 'Calculating production package integrity manifest...'
& (Join-Path $repoRoot 'scripts\New-ProductionPackageManifest.ps1') `
    -PackageRoot $packageRoot -Version $Version

if (-not $SkipInstaller) {
    if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
        $candidates = @(
            $env:INNO_SETUP_COMPILER,
            'C:\Program Files\Inno Setup 7\ISCC.exe',
            'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
            'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
        )
        $InnoCompiler = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
    }
    if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
        throw 'Inno Setup Compiler 7 is required to produce Setup.exe. Set -InnoCompiler or INNO_SETUP_COMPILER.'
    }
    & $InnoCompiler /Q "/DSourceRoot=$packageRoot" "/DOutputDir=$installerOutput" "/DAppVersion=$Version" `
        (Join-Path $repoRoot 'installer\TGExhibition.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }
    $setup = Get-ChildItem -LiteralPath $installerOutput -Filter '*_Setup.exe' -File | Select-Object -First 1
    if ($null -eq $setup) { throw "Installer output was not created in: $installerOutput" }
}

Write-Host "Production package ready: $packageRoot"
if (-not $SkipInstaller) { Write-Host "Offline installer ready: $($setup.FullName)" }
