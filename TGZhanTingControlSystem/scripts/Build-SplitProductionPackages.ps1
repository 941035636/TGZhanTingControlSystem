[CmdletBinding()]
param(
    [string]$OutputRoot = 'artifacts\ThreeMachineDeployment',
    [string]$Version = '1.0.0',
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2020.3.35f1c2\Editor\Unity.exe',
    [string]$ExistingUnityBuildRoot,
    [string]$MeloTtsBundleSource,
    [string]$InnoCompiler,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = if ([IO.Path]::IsPathRooted($OutputRoot)) {
    [IO.Path]::GetFullPath($OutputRoot)
} else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot)) }
$packagesRoot = Join-Path $artifactRoot 'Packages'
$serverPackage = Join-Path $packagesRoot 'Server'
$touchPackage = Join-Path $packagesRoot 'Touch'
$ledPackage = Join-Path $packagesRoot 'Led'
$installerOutput = Join-Path $artifactRoot 'Installers'
$stagingRoot = Join-Path $artifactRoot 'Staging'

function Reset-ArtifactDirectory([string]$path) {
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath($path)
    if (-not $resolved.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside the repository artifacts root: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        $empty = Join-Path ([IO.Path]::GetTempPath()) ('TG-Empty-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $empty | Out-Null
        try {
            & "$env:SystemRoot\System32\robocopy.exe" $empty $resolved /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "Could not reset artifact directory (robocopy $LASTEXITCODE): $resolved" }
        } finally { Remove-Item -LiteralPath $empty -Force }
    } else { New-Item -ItemType Directory -Path $resolved | Out-Null }
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
    } finally { $env:TG_WINDOWS_BUILD_OUTPUT = $previousOutput }
}

function Assert-Files([string]$root, [string[]]$relativePaths) {
    $missing = @($relativePaths | Where-Object { -not (Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf) })
    if ($missing.Count -gt 0) { throw "Package is incomplete: $root`n$($missing -join "`n")" }
}

Reset-ArtifactDirectory $artifactRoot
New-Item -ItemType Directory -Path $packagesRoot,$serverPackage,$touchPackage,$ledPackage,$installerOutput,$stagingRoot | Out-Null

Write-Host 'Publishing self-contained Server and embedded AdminWeb...'
dotnet publish (Join-Path $repoRoot 'src\Server\TG.Control.Server\TG.Control.Server.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:DebugSymbols=false -p:DebugType=None --output (Join-Path $serverPackage 'Server')
if ($LASTEXITCODE -ne 0) { throw 'Server production publish failed.' }

Write-Host 'Publishing the terminal Runtime Launcher...'
$launcherStaging = Join-Path $stagingRoot 'Launcher'
dotnet publish (Join-Path $repoRoot 'src\Launcher\TG.Control.Launcher\TG.Control.Launcher.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugSymbols=false -p:DebugType=None --output $launcherStaging
if ($LASTEXITCODE -ne 0) { throw 'Runtime Launcher production publish failed.' }
Copy-Directory $launcherStaging (Join-Path $touchPackage 'Launcher')
Copy-Directory $launcherStaging (Join-Path $ledPackage 'Launcher')

if ([string]::IsNullOrWhiteSpace($ExistingUnityBuildRoot)) {
    $unityOutput = Join-Path $artifactRoot 'UnityBuilds'
    New-Item -ItemType Directory -Path $unityOutput | Out-Null
    Invoke-UnityBuild (Join-Path $repoRoot 'src\TouchClient') 'TG.Control.Editor.WindowsPlayerBuilder.Build' `
        (Join-Path $unityOutput 'TouchClient\TouchClient.exe') (Join-Path $artifactRoot 'TouchClient-build.log')
    Invoke-UnityBuild (Join-Path $repoRoot 'src\LedPlayer') 'TG.Control.Editor.WindowsPlayerBuilder.Build' `
        (Join-Path $unityOutput 'LedPlayer\LedPlayer.exe') (Join-Path $artifactRoot 'LedPlayer-build.log')
} else {
    $unityOutput = if ([IO.Path]::IsPathRooted($ExistingUnityBuildRoot)) {
        [IO.Path]::GetFullPath($ExistingUnityBuildRoot)
    } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $ExistingUnityBuildRoot)) }
}
Copy-Directory (Join-Path $unityOutput 'TouchClient') (Join-Path $touchPackage 'TouchClient')
Copy-Directory (Join-Path $unityOutput 'LedPlayer') (Join-Path $ledPackage 'LedPlayer')

if ([string]::IsNullOrWhiteSpace($MeloTtsBundleSource)) {
    $meloDestination = Join-Path $serverPackage 'TtsWorker\MeloTtsLocal'
    & (Join-Path $repoRoot 'scripts\Build-MeloTtsWindowsBundle.ps1') -DestinationRoot $meloDestination
    if ($LASTEXITCODE -ne 0) { throw 'MeloTTS offline bundle build failed.' }
} else {
    $resolvedMeloSource = if ([IO.Path]::IsPathRooted($MeloTtsBundleSource)) {
        [IO.Path]::GetFullPath($MeloTtsBundleSource)
    } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $MeloTtsBundleSource)) }
    Copy-Directory $resolvedMeloSource (Join-Path $serverPackage 'TtsWorker\MeloTtsLocal')
}

foreach ($package in @($serverPackage,$touchPackage,$ledPackage)) {
    Copy-Directory (Join-Path $repoRoot 'scripts\deployment') (Join-Path $package 'Tools')
    Copy-Directory (Join-Path $repoRoot 'ThirdParty') (Join-Path $package 'ThirdParty')
}

Assert-Files $serverPackage @(
    'Server\TG.Control.Server.exe','Server\AdminWeb\index.html',
    'TtsWorker\MeloTtsLocal\worker.py','TtsWorker\MeloTtsLocal\runtime\python.exe',
    'TtsWorker\MeloTtsLocal\models\MeloTTS-Chinese\checkpoint.pth',
    'TtsWorker\MeloTtsLocal\models\bert-base-multilingual-uncased\pytorch_model.bin',
    'Tools\Install-TGServer.ps1','Tools\Uninstall-TGExhibition.ps1','ThirdParty\NOTICE.md')
Assert-Files $touchPackage @(
    'TouchClient\TouchClient.exe','TouchClient\TouchClient_Data\globalgamemanagers',
    'Launcher\TG.Control.Launcher.exe','Tools\Install-TGTerminal.ps1','Tools\Uninstall-TGTerminal.ps1','ThirdParty\NOTICE.md')
Assert-Files $ledPackage @(
    'LedPlayer\LedPlayer.exe','LedPlayer\LedPlayer_Data\globalgamemanagers',
    'LedPlayer\LedPlayer_Data\Plugins\x86_64\AVProVideo.dll',
    'LedPlayer\LedPlayer_Data\Plugins\x86_64\AVProVideoWinRT.dll',
    'LedPlayer\LedPlayer_Data\Plugins\x86_64\Audio360.dll',
    'Launcher\TG.Control.Launcher.exe','Tools\Install-TGTerminal.ps1','Tools\Uninstall-TGTerminal.ps1','ThirdParty\NOTICE.md')

foreach ($package in @($serverPackage,$touchPackage,$ledPackage)) {
    & (Join-Path $repoRoot 'scripts\New-ProductionPackageManifest.ps1') -PackageRoot $package -Version $Version
}

$templateRoot = Join-Path $repoRoot 'scripts\deployment\templates'
Copy-Item -LiteralPath (Join-Path $templateRoot 'server.site-install.json') -Destination $installerOutput
Copy-Item -LiteralPath (Join-Path $templateRoot 'touch.site-install.json') -Destination $installerOutput
Copy-Item -LiteralPath (Join-Path $templateRoot 'led.site-install.json') -Destination $installerOutput

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
        throw 'Inno Setup Compiler is required to produce Setup.exe. Set -InnoCompiler or INNO_SETUP_COMPILER.'
    }
    # The embedded Python environment contains valid relative paths that exceed MAX_PATH when the repository
    # itself is deeply nested. A temporary drive mapping keeps Inno Setup's source paths below that limit.
    $buildDrive = $null
    foreach ($letter in @('R','Q','P','O','N')) {
        if (-not (Test-Path -LiteralPath ($letter + ':\'))) { $buildDrive = $letter + ':'; break }
    }
    if ($null -eq $buildDrive) { throw 'No free temporary drive letter is available for installer compilation.' }
    & "$env:SystemRoot\System32\subst.exe" $buildDrive $artifactRoot
    if ($LASTEXITCODE -ne 0) { throw "Could not create temporary build drive: $buildDrive" }
    try {
        $shortOutput = Join-Path ($buildDrive + '\') 'Installers'
        $installerDefinitions = @(
            @{ Package = 'Server'; Script = 'TGExhibition.Server.iss' },
            @{ Package = 'Touch'; Script = 'TGExhibition.Touch.iss' },
            @{ Package = 'Led'; Script = 'TGExhibition.Led.iss' }
        )
        foreach ($definition in $installerDefinitions) {
            $shortSource = Join-Path ($buildDrive + '\') ('Packages\' + $definition.Package)
            & $InnoCompiler /Q "/DSourceRoot=$shortSource" "/DOutputDir=$shortOutput" `
                "/DAppVersion=$Version" (Join-Path $repoRoot ('installer\' + $definition.Script))
            if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed: $($definition.Script)" }
        }
    } finally {
        & "$env:SystemRoot\System32\subst.exe" $buildDrive /D | Out-Null
    }
}

& (Join-Path $repoRoot 'scripts\Test-SplitProductionPackages.ps1') -PackagesRoot $packagesRoot
Write-Host "Three-machine packages are ready: $packagesRoot"
if (-not $SkipInstaller) { Write-Host "Three offline installers are ready: $installerOutput" }
