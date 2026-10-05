# Sierra Business head office on Supabase (trial or production).
# Runs the head-office server on this computer against your Supabase PostgreSQL database; branch computers sync with it.
# The database password is kept only in this Windows account's protected storage (DPAPI) under App_Data\head-office.json.
param(
    [int]$Port = 5180,
    [string]$Branch = 'HQ',
    [string[]]$Branches = @('FREETOWN'),
    [switch]$Reset
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'head-office-connection.ps1')
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$settingsPath = Join-Path $root 'App_Data\head-office.json'
New-Item -ItemType Directory -Force (Split-Path -Parent $settingsPath) | Out-Null

function New-SyncKey { $bytes = New-Object byte[] 20; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); return -join ($bytes | ForEach-Object { $_.ToString('x2') }) }

$settings = $null
if ((Test-Path -LiteralPath $settingsPath) -and !$Reset) { $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json }
if ($null -eq $settings) {
    Write-Output 'First run. In Supabase open your project, click Connect, choose "Session pooler", and copy the host and user from it.'
    $poolerHost = Read-Host 'Session pooler host (for example aws-0-eu-west-3.pooler.supabase.com)'
    $user = Read-Host 'User (for example postgres.wmlubgelbkqgqqgbnfwb)'
    $password = Read-Host 'Database password' -AsSecureString
    $settings = [pscustomobject]@{ Host = $poolerHost.Trim(); User = $user.Trim(); Password = ($password | ConvertFrom-SecureString); Keys = [pscustomobject]@{}; OwnerCreated = $false }
}
foreach ($code in $Branches) {
    $name = $code.Trim().ToUpperInvariant()
    if (-not ($settings.Keys.PSObject.Properties.Name -contains $name)) { $settings.Keys | Add-Member -NotePropertyName $name -NotePropertyValue (New-SyncKey) }
}
$settings | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $settingsPath -Encoding UTF8

$plain = [System.Net.NetworkCredential]::new('', ($settings.Password | ConvertTo-SecureString)).Password
$env:Database__Provider = 'Supabase'
$env:ConnectionStrings__Business = New-SierraPostgresConnectionString -DatabaseHost $settings.Host -Username $settings.User -Password $plain
$env:Trading__Branch = $Branch
$env:Sync__Role = 'Central'
foreach ($p in $settings.Keys.PSObject.Properties) { Set-Item -Path ("Env:Sync__Keys__" + $p.Name) -Value $p.Value }

Push-Location (Join-Path $root 'src\SierraBusiness.Web')
try {
    & $dotnet build -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Write-Output 'Creating or upgrading the database tables in Supabase (schema "sierra")...'
    & $dotnet run -c Release --no-build --no-launch-profile -- --migrate
    if ($LASTEXITCODE -ne 0) { throw 'The database could not be prepared. Check the host, user and password (run again with -Reset to re-enter them).' }
    if (-not $settings.OwnerCreated) {
        $email = Read-Host 'Head-office owner email (for signing in to the head-office screens)'
        $secure = Read-Host 'Owner password (at least 12 characters, upper/lowercase, number and symbol)' -AsSecureString
        $env:Bootstrap__Email = $email
        $env:Bootstrap__Password = [System.Net.NetworkCredential]::new('', $secure).Password
        try { & $dotnet run -c Release --no-build --no-launch-profile -- --create-owner; if ($LASTEXITCODE -ne 0) { throw 'Owner creation failed.' } }
        finally { Remove-Item Env:Bootstrap__Password; Remove-Item Env:Bootstrap__Email }
        $settings.OwnerCreated = $true
        $settings | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    }
    Write-Output ''
    Write-Output "Head office is starting on http://localhost:$Port"
    Write-Output 'On each branch computer: Administration > Branch sync > head office address and the branch key below, then restart Sierra Business.'
    Write-Output '(A branch on this same computer can use http://localhost; other computers need an https address in front of this server.)'
    foreach ($p in $settings.Keys.PSObject.Properties) { Write-Output ("  Branch {0}: key {1}" -f $p.Name, $p.Value) }
    Write-Output 'Keep this window open while branches sync. Press Ctrl+C to stop.'
    & $dotnet run -c Release --no-build --no-launch-profile --urls "http://localhost:$Port"
} finally {
    Pop-Location
    Remove-Item Env:ConnectionStrings__Business -ErrorAction SilentlyContinue
}
