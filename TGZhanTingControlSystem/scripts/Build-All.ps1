$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$admin = Join-Path $root 'src\AdminWeb'
$server = Join-Path $root 'src\Server\TG.Control.Server'
$wwwroot = Join-Path $server 'wwwroot'

Push-Location $admin
try {
    npm install
    npm run build
} finally {
    Pop-Location
}

if (Test-Path -LiteralPath $wwwroot) {
    Remove-Item -LiteralPath $wwwroot -Recurse -Force
}
New-Item -ItemType Directory -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $admin 'dist\*') -Destination $wwwroot -Recurse -Force

dotnet build (Join-Path $server 'TG.Control.Server.csproj') --configuration Release
