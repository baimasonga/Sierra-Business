param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$PreviousInstaller,
    [ValidatePattern('^(\d+\.\d+\.\d+)?$')][string]$PreviousVersion,
    [Parameter(Mandatory)][string]$Driver,
    [string]$OutputDirectory = 'artifacts/windows/acceptance'
)
# Clean-machine acceptance for the Windows installer: silent install, first launch, keyboard-only owner setup, accessibility and
# display-scaling checks, restart, upgrade (from the previous release when one is given, otherwise a reinstall over itself) and
# uninstall. It installs into the current Windows account and creates an owner account, so it refuses to run where Rokela Business
# Admin or its data already exist: use a disposable machine or account (the release workflow uses a fresh runner).
$ErrorActionPreference = 'Stop'
if ([bool]$PreviousInstaller -ne [bool]$PreviousVersion) { throw 'Pass -PreviousInstaller and -PreviousVersion together.' }
$installerName = 'RokelaBusinessAdmin-Setup-x64.exe'
$appKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{FCDE1608-8326-49E7-855D-35ED950294CC}_is1'
$appDir = Join-Path $env:LOCALAPPDATA 'Programs\Rokela Business Admin'
$exe = Join-Path $appDir 'SierraBusiness.Maui.exe'
$dataRoot = Join-Path $env:LOCALAPPDATA 'SierraBusiness'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Rokela Business Admin\Rokela Business Admin.lnk'
$port = 9333
if ((Test-Path -LiteralPath $appKey) -or (Test-Path -LiteralPath $appDir) -or (Test-Path -LiteralPath $dataRoot)) {
    throw 'Rokela Business Admin or its data already exist in this Windows account. Run acceptance only on a clean machine or account.'
}
$out = (New-Item -ItemType Directory -Force $OutputDirectory).FullName
$checks = [Collections.Generic.List[object]]::new()
$failed = $false
function Record([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([pscustomobject]@{ name = $Name; passed = $Passed; detail = $Detail })
    Write-Output "$(if ($Passed) { 'PASS' } else { 'FAIL' }): $Name - $Detail"
    if (!$Passed) { $script:failed = $true; throw "$Name failed: $Detail" }
}

function Test-Checksum([string]$File) {
    $sums = Join-Path (Split-Path -Parent $File) 'SHA256SUMS.txt'
    $hash = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash
    Record "Checksum of $File" ((Get-Content -LiteralPath $sums -Raw).Trim() -eq "$hash  $installerName") $hash
}
function Install-Package([string]$File, [string]$Label) {
    $log = Join-Path $out "$Label-setup.log"
    $process = Start-Process -FilePath $File -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/LOG=`"$log`"" -Wait -PassThru
    Record "$Label`: setup finished" ($process.ExitCode -eq 0) "Exit code $($process.ExitCode)"
}
function Test-Installed([string]$Expected, [string]$Label) {
    Record "$Label`: program installed" (Test-Path -LiteralPath $exe -PathType Leaf) $exe
    $product = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion.Split('+')[0]
    Record "$Label`: program version" ($product -eq $Expected) $product
    $registered = (Get-ItemProperty -LiteralPath $appKey).DisplayVersion
    Record "$Label`: Apps & features entry" ($registered -eq $Expected) "DisplayVersion $registered"
    $target = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut).TargetPath
    Record "$Label`: Start menu shortcut" ($target -eq $exe) $target
}
function Get-DataFiles { @(Get-ChildItem -LiteralPath (Join-Path $dataRoot 'Data') -Recurse -File -Filter 'sierra.db' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName.Substring($dataRoot.Length) } | Sort-Object) }

# Launch the program, let the driver work in its window, then close it the way a user does and require a clean exit.
function Invoke-Session([string]$Step, [string]$Label) {
    # Remote debugging is enabled only for this process, so the driver can reach the embedded page; installed copies never have it.
    $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$port"
    try { $process = Start-Process -FilePath $exe -WorkingDirectory $appDir -PassThru } finally { Remove-Item Env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS }
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    do { Start-Sleep -Milliseconds 500; $process.Refresh() } while (!$process.HasExited -and ($process.MainWindowHandle -eq 0 -or !$process.MainWindowTitle) -and [DateTime]::UtcNow -lt $deadline)
    Record "$Label`: window opens" (!$process.HasExited -and $process.MainWindowHandle -ne 0) "Title '$($process.MainWindowTitle)'"
    Record "$Label`: window title" ($process.MainWindowTitle -eq 'Rokela Business Admin') $process.MainWindowTitle
    & node $Driver --step $Step --port $port --out $out --label $Label
    $driverExit = $LASTEXITCODE
    $process.Refresh()
    Record "$Label`: still responding" (!$process.HasExited -and $process.Responding) 'After the driver finished'
    Record "$Label`: $Step in the window" ($driverExit -eq 0) "Driver exit code $driverExit (details in $Label-$Step.json)"
    [void]$process.CloseMainWindow()
    $closed = $process.WaitForExit(30000)
    if (!$closed) { $process.Kill() }
    Record "$Label`: closes when the window is closed" $closed 'Within 30 seconds'
    Start-Sleep -Seconds 2
    Record "$Label`: no copy left running" (@(Get-Process -Name 'SierraBusiness.Maui' -ErrorAction SilentlyContinue).Count -eq 0) 'SierraBusiness.Maui'
}

$env:ACCEPT_EMAIL = 'owner@example.com'
$env:ACCEPT_PASSWORD = 'Ra1!' + [Guid]::NewGuid().ToString('N')   # meets the 12-character mixed rule; never written to the report
try {
    Test-Checksum $Installer
    if ($PreviousInstaller) {
        Test-Checksum $PreviousInstaller
        Install-Package $PreviousInstaller 'previous'
        Test-Installed $PreviousVersion 'previous'
        Invoke-Session 'setup' 'previous'
        $before = Get-DataFiles
        Record 'previous: company database created' ($before.Count -gt 0) ($before -join ', ')
        Install-Package $Installer 'upgrade'
        Test-Installed $Version 'upgrade'
        Invoke-Session 'inspect' 'upgrade'
        $label = 'upgrade'
    } else {
        Install-Package $Installer 'install'
        Test-Installed $Version 'install'
        Invoke-Session 'setup' 'first-launch'
        $before = Get-DataFiles
        Record 'first-launch: company database created' ($before.Count -gt 0) ($before -join ', ')
        Invoke-Session 'inspect' 'restart'
        Install-Package $Installer 'reinstall'
        Test-Installed $Version 'reinstall'
        $label = 'reinstall'
    }
    $after = Get-DataFiles
    Record "$label`: company data kept" (@($before | Where-Object { $after -notcontains $_ }).Count -eq 0) ($after -join ', ')
    Invoke-Session 'signin' "$label-restart"

    $uninstaller = (Get-ItemProperty -LiteralPath $appKey).UninstallString.Trim('"')
    Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
    # The uninstaller hands over to a temporary copy of itself; wait for that to finish.
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while (((Test-Path -LiteralPath $exe) -or (Test-Path -LiteralPath $appKey)) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Seconds 1 }
    Record 'uninstall: program removed' (!(Test-Path -LiteralPath $exe)) $appDir
    Record 'uninstall: Apps & features entry removed' (!(Test-Path -LiteralPath $appKey)) $appKey
    Record 'uninstall: Start menu shortcut removed' (!(Test-Path -LiteralPath $shortcut)) $shortcut
    $remaining = Get-DataFiles
    Record 'uninstall: company data kept' (@($before | Where-Object { $remaining -notcontains $_ }).Count -eq 0) ($remaining -join ', ')
} catch {
    if (!$failed) { $checks.Add([pscustomobject]@{ name = 'Acceptance script'; passed = $false; detail = $_.Exception.Message }) }
    $failed = $true
    Write-Output "Stopped: $($_.Exception.Message)"
} finally {
    Remove-Item Env:ACCEPT_PASSWORD, Env:ACCEPT_EMAIL -ErrorAction SilentlyContinue
    Get-Process -Name 'SierraBusiness.Maui' -ErrorAction SilentlyContinue | Stop-Process -Force
}

$windows = Get-CimInstance Win32_OperatingSystem
$report = [pscustomobject]@{
    version = $Version
    upgradedFrom = if ($PreviousVersion) { $PreviousVersion } else { $null }
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    machine = "$($windows.Caption) $($windows.Version)"
    passed = !$failed
    signature = (Get-AuthenticodeSignature -LiteralPath $Installer).Status.ToString()
    checks = $checks
    notCovered = @(
        'WebView2 was already present on the test machine, so the bundled WebView2 installer was not exercised.',
        'Display scaling is emulated inside the window (125%, 150%, 200%); Windows display settings were not changed.',
        'Screen readers, physical printers, scanners, cash drawers and cheque alignment need a person and the hardware.',
        'Network outages between two branch computers need two machines.'
    )
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $out 'acceptance.json') -Encoding utf8
$lines = @("## Clean-machine acceptance: $(if ($failed) { 'FAILED' } else { 'passed' })", '',
    "Tested on a fresh $($report.machine) runner$(if ($PreviousVersion) { ", upgrading from $PreviousVersion" })." , '')
$lines += $checks | ForEach-Object { "- $(if ($_.passed) { '✅' } else { '❌' }) $($_.name)" }
$lines += '', '**Not covered by this automated check:**', ''
$lines += $report.notCovered | ForEach-Object { "- $_" }
$lines | Set-Content -LiteralPath (Join-Path $out 'acceptance.md') -Encoding utf8
if ($failed) { exit 1 }
Write-Output "PASS: $($checks.Count) clean-machine acceptance checks."
