param([Parameter(Mandatory=$true)][string]$Email)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
Push-Location $root
try {
    & $dotnet restore SierraBusiness.sln
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & $dotnet build SierraBusiness.sln --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Push-Location 'src\SierraBusiness.Web'
    try {
        & $dotnet run --no-build -- --migrate
        if ($LASTEXITCODE -ne 0) { throw 'Migration failed.' }
        $secure = Read-Host 'Choose an owner password (at least 12 characters, upper/lowercase, number and symbol)' -AsSecureString
        $env:Bootstrap__Email = $Email
        $env:Bootstrap__Password = [System.Net.NetworkCredential]::new('', $secure).Password
        try { & $dotnet run --no-build -- --create-owner; if ($LASTEXITCODE -ne 0) { throw 'Owner creation failed.' } }
        finally { Remove-Item Env:Bootstrap__Password; Remove-Item Env:Bootstrap__Email }
    } finally { Pop-Location }
} finally { Pop-Location }
