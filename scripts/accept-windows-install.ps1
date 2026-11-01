param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$PreviousInstaller,
    [ValidatePattern('^(\d+\.\d+\.\d+)?$')][string]$PreviousVersion,
    [string]$OutputDirectory = 'artifacts/windows/acceptance'
)
# Clean-machine acceptance for the Windows installer: silent install, first launch (the page is drawn, also at the minimum window
# size, and the company database is created), restart, upgrade (from the previous release when one is given, otherwise a reinstall over
# itself) and uninstall, with company data kept throughout. The installed program is not changed or started in any special mode. It
# installs into the current Windows account, so it refuses to run where Rokela Business Admin or its data already exist: use a
# disposable machine or account (the release workflow uses a fresh runner).
$ErrorActionPreference = 'Stop'
if ([bool]$PreviousInstaller -ne [bool]$PreviousVersion) { throw 'Pass -PreviousInstaller and -PreviousVersion together.' }
$installerName = 'RokelaBusinessAdmin-Setup-x64.exe'
$appKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{FCDE1608-8326-49E7-855D-35ED950294CC}_is1'
$appDir = Join-Path $env:LOCALAPPDATA 'Programs\Rokela Business Admin'
$exe = Join-Path $appDir 'SierraBusiness.Maui.exe'
$dataRoot = Join-Path $env:LOCALAPPDATA 'SierraBusiness'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Rokela Business Admin\Rokela Business Admin.lnk'
if ((Test-Path -LiteralPath $appKey) -or (Test-Path -LiteralPath $appDir) -or (Test-Path -LiteralPath $dataRoot)) {
    throw 'Rokela Business Admin or its data already exist in this Windows account. Run acceptance only on a clean machine or account.'
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Windows.Forms, System.Drawing
$Ui = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
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

# --- The program window -------------------------------------------------------------------------------------------------------------

function Save-WindowShot($Window, [string]$Name) {
    $r = $Window.Current.BoundingRectangle
    $bitmap = New-Object Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen([int]$r.Left, [int]$r.Top, 0, 0, $bitmap.Size)
    $graphics.Dispose()
    $bitmap.Save((Join-Path $out "$Name.png"), [Drawing.Imaging.ImageFormat]::Png)
    $bitmap
}

# A page that failed to load leaves the window blank or a single colour. Count the distinct colours on a grid below the title bar:
# the sign-in screen's gradient, logo, text and fields give hundreds; a blank window gives one or two.
function Get-ColourVariety($Bitmap) {
    $colours = [Collections.Generic.HashSet[int]]::new()
    for ($y = 60; $y -lt $Bitmap.Height - 10; $y += 6) { for ($x = 10; $x -lt $Bitmap.Width - 10; $x += 6) { [void]$colours.Add($Bitmap.GetPixel($x, $y).ToArgb()) } }
    $colours.Count
}

# When a session fails, keep the whole desktop and the window's automation tree.
function Save-Diagnostics($Window, [string]$Label) {
    try {
        $bounds = [Windows.Forms.SystemInformation]::VirtualScreen
        $bitmap = New-Object Drawing.Bitmap $bounds.Width, $bounds.Height
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($bounds.Location, [Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save((Join-Path $out "$Label-desktop.png"), [Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose(); $bitmap.Dispose()
    } catch { Write-Output "Desktop screenshot failed: $($_.Exception.Message)" }
    try {
        $lines = foreach ($e in $Window.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            "$($e.Current.ControlType.ProgrammaticName)`t$($e.Current.Name)`t$($e.Current.AutomationId)"
        }
        @($lines | Select-Object -First 600) | Set-Content -LiteralPath (Join-Path $out "$Label-tree.txt") -Encoding utf8
    } catch { Write-Output "Automation tree dump failed: $($_.Exception.Message)" }
}

# Screen readers find the page through Windows UI Automation. Record whether the page's heading is reachable from the window; this is a
# finding for people to follow up with a screen reader, not a pass/fail check (the embedded browser's tree can appear only when a
# screen reader is running).
$automation = [ordered]@{}
function Find-PageText($Window, [string]$Text, [int]$Seconds) {
    $condition = [System.Windows.Automation.PropertyCondition]::new($Ui::NameProperty, $Text)
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if ($Window.FindFirst($Scope::Descendants, $condition)) { return $true }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    $false
}

# Launch the program, check the window shows the page, then close it the way a user does and require a clean exit.
function Invoke-Session([string]$Label, [string]$Heading, [switch]$Inspect) {
    $process = Start-Process -FilePath $exe -WorkingDirectory $appDir -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    do { Start-Sleep -Milliseconds 500; $process.Refresh() } while (!$process.HasExited -and ($process.MainWindowHandle -eq 0 -or !$process.MainWindowTitle) -and [DateTime]::UtcNow -lt $deadline)
    Record "$Label`: window opens" (!$process.HasExited -and $process.MainWindowHandle -ne 0) "Title '$($process.MainWindowTitle)'"
    Record "$Label`: window title" ($process.MainWindowTitle -eq 'Rokela Business Admin') $process.MainWindowTitle
    $window = $Ui::FromHandle($process.MainWindowHandle)
    try {
        Start-Sleep -Seconds 15   # let the embedded page load and the local workspace start
        $shot = Save-WindowShot $window "$Label-window"
        $variety = Get-ColourVariety $shot; $shot.Dispose()
        Record "$Label`: page drawn in the window" ($variety -ge 50) "$variety distinct colours (a blank window has 1-2)"
        if ($Inspect) {
            $automation["$Label`: '$Heading' reachable through UI Automation"] = Find-PageText $window $Heading 30
            $transform = $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
            $transform.Resize(800, 600)
            Start-Sleep -Seconds 2
            $shot = Save-WindowShot $window "$Label-window-800x600"
            $variety = Get-ColourVariety $shot; $shot.Dispose()
            Record "$Label`: page drawn at the minimum window size" ($variety -ge 50) "$variety distinct colours"
        }
    } catch {
        Save-Diagnostics $window $Label
        throw
    }
    $process.Refresh()
    Record "$Label`: still responding" (!$process.HasExited -and $process.Responding) 'After the session'
    [void]$process.CloseMainWindow()
    $closed = $process.WaitForExit(30000)
    if (!$closed) { $process.Kill() }
    Record "$Label`: closes when the window is closed" $closed 'Within 30 seconds'
    Start-Sleep -Seconds 2
    Record "$Label`: no copy left running" (@(Get-Process -Name 'SierraBusiness.Maui' -ErrorAction SilentlyContinue).Count -eq 0) 'SierraBusiness.Maui'
}

try {
    Test-Checksum $Installer
    if ($PreviousInstaller) {
        Test-Checksum $PreviousInstaller
        Install-Package $PreviousInstaller 'previous'
        Test-Installed $PreviousVersion 'previous'
        Invoke-Session 'previous' 'Set up your business.'
        $before = Get-DataFiles
        Record 'previous: company database created' ($before.Count -gt 0) ($before -join ', ')
        Install-Package $Installer 'upgrade'
        Test-Installed $Version 'upgrade'
        Invoke-Session 'upgrade' 'Set up your business.' -Inspect
        $label = 'upgrade'
    } else {
        Install-Package $Installer 'install'
        Test-Installed $Version 'install'
        Invoke-Session 'first-launch' 'Set up your business.' -Inspect
        $before = Get-DataFiles
        Record 'first-launch: company database created' ($before.Count -gt 0) ($before -join ', ')
        Invoke-Session 'restart' 'Set up your business.'
        Install-Package $Installer 'reinstall'
        Test-Installed $Version 'reinstall'
        $label = 'reinstall'
    }
    $after = Get-DataFiles
    Record "$label`: company data kept" (@($before | Where-Object { $after -notcontains $_ }).Count -eq 0) ($after -join ', ')
    Invoke-Session "$label-restart" 'Set up your business.'

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
    Get-Process -Name 'SierraBusiness.Maui' -ErrorAction SilentlyContinue | Stop-Process -Force
}

$windows = Get-CimInstance Win32_OperatingSystem
$screen = [Windows.Forms.SystemInformation]::PrimaryMonitorSize
$report = [pscustomobject]@{
    version = $Version
    upgradedFrom = if ($PreviousVersion) { $PreviousVersion } else { $null }
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    machine = "$($windows.Caption) $($windows.Version), $($screen.Width) x $($screen.Height) screen"
    passed = !$failed
    signature = (Get-AuthenticodeSignature -LiteralPath $Installer).Status.ToString()
    checks = $checks
    uiAutomation = $automation
    notCovered = @(
        'Signing in, typing into the screens and keyboard order were not exercised: the embedded page did not appear in the window''s UI Automation tree on the test machine (see uiAutomation), so a person should check sign-in, the keyboard and a screen reader (Narrator) by hand.',
        'WebView2 was already present on the test machine, so the bundled WebView2 installer was not exercised.',
        'Layout was checked only as a drawn page at the minimum window size (screenshots attached); Windows display-scaling settings were not changed.',
        'Physical printers, scanners, cash drawers and cheque alignment need a person and the hardware.',
        'Network outages between two branch computers need two machines.'
    )
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $out 'acceptance.json') -Encoding utf8
$lines = @("## Clean-machine acceptance: $(if ($failed) { 'FAILED' } else { 'passed' })", '',
    "Tested on a fresh $($report.machine) runner$(if ($PreviousVersion) { ", upgrading from $PreviousVersion" }).", '')
$lines += $checks | ForEach-Object { "- $(if ($_.passed) { '✅' } else { '❌' }) $($_.name)" }
if ($automation.Count) { $lines += '', '**UI Automation findings (not pass/fail):**', ''; $lines += $automation.GetEnumerator() | ForEach-Object { "- $($_.Key): $(if ($_.Value) { 'yes' } else { 'no' })" } }
$lines += '', '**Not covered by this automated check:**', ''
$lines += $report.notCovered | ForEach-Object { "- $_" }
$lines | Set-Content -LiteralPath (Join-Path $out 'acceptance.md') -Encoding utf8
if ($failed) { exit 1 }
Write-Output "PASS: $($checks.Count) clean-machine acceptance checks."
