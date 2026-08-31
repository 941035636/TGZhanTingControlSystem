$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$server = Join-Path $root 'src\Server\TG.Control.Server'

# The Server project owns the deterministic AdminWeb build and output copy so
# dotnet build, dotnet run, dotnet publish and this aggregate script behave alike.
dotnet build (Join-Path $server 'TG.Control.Server.csproj') --configuration Release
