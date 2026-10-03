param([int]$Port = 5180)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
Push-Location (Join-Path $root 'src\SierraBusiness.Web')
try { & $dotnet run --no-launch-profile --urls "http://localhost:$Port" } finally { Pop-Location }
