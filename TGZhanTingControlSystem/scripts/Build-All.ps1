$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$server = Join-Path $root 'src\Server\TG.Control.Server'

# The Server project owns the deterministic AdminWeb build and output copy so
# dotnet build, dotnet run, dotnet publish and this aggregate script behave alike.
dotnet build (Join-Path $server 'TG.Control.Server.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Server build failed.' }

$launcher = Join-Path $root 'src\Launcher\TG.Control.Launcher\TG.Control.Launcher.csproj'
dotnet build $launcher --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Runtime Launcher build failed.' }
