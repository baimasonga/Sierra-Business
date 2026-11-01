param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$PreviousInstaller,
    [ValidatePattern('^(\d+\.\d+\.\d+)?$')][string]$PreviousVersion,
    [string]$OutputDirectory = 'artifacts/windows/acceptance'
)
# Clean-machine acceptance for the Windows installer: silent install, first launch, keyboard-only owner setup, accessible names and
# layout at the minimum window size, restart, upgrade (from the previous release when one is given, otherwise a reinstall over itself)
# and uninstall. It works the window the way assistive technology does, through Windows UI Automation, and types with real keystrokes;
# the installed program is not changed or started in any special mode. It installs into the current Windows account and creates an
# owner account, so it refuses to run where Rokela Business Admin or its data already exist: use a disposable machine or account (the
# release workflow uses a fresh runner).
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
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AcceptanceWindow
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
$Ui = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$out = (New-Item -ItemType Directory -Force $OutputDirectory).FullName
$checks = [Collections.Generic.List[object]]::new()
$failed = $false
$email = 'owner@example.com'
$password = 'Ra1!' + [Guid]::NewGuid().ToString('N')   # meets the 12-character mixed rule; never written out

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

# --- Working the window through UI Automation ---------------------------------------------------------------------------------------

function New-Condition([string]$Name, [System.Windows.Automation.ControlType]$ControlType) {
    $named = [System.Windows.Automation.PropertyCondition]::new($Ui::NameProperty, $Name)
    if (!$ControlType) { return $named }
    $typed = [System.Windows.Automation.PropertyCondition]::new($Ui::ControlTypeProperty, $ControlType)
    [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.Condition[]]@($named, $typed))
}
function Find-Named($Root, [string]$Name, [System.Windows.Automation.ControlType]$ControlType = $null, [int]$Seconds = 60) {
    $condition = New-Condition $Name $ControlType
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $element = $Root.FindFirst($Scope::Descendants, $condition)
        if ($element) { return $element }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    $null
}
function Get-FocusedName { try { $Ui::FocusedElement.Current.Name } catch { '' } }
function Send-Keys([string]$Keys) { [System.Windows.Forms.SendKeys]::SendWait($Keys); Start-Sleep -Milliseconds 200 }
function Send-Text([string]$Text) { Send-Keys ([regex]::Replace($Text, '[+^%~(){}\[\]]', '{$0}')) }

function Save-WindowShot($Window, [string]$Name) {
    try {
        $r = $Window.Current.BoundingRectangle
        $bitmap = New-Object Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen([int]$r.Left, [int]$r.Top, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path $out "$Name.png"), [Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose(); $bitmap.Dispose()
    } catch { Write-Output "Screenshot $Name failed: $($_.Exception.Message)" }
}

# When a step fails, keep what explains it: the whole desktop and the window's automation tree.
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
            "$($e.Current.ControlType.ProgrammaticName)`t$($e.Current.Name)`t$($e.Current.AutomationId)`toffscreen=$($e.Current.IsOffscreen)"
        }
        @("Focused: $(Get-FocusedName)") + @($lines | Select-Object -First 600) | Set-Content -LiteralPath (Join-Path $out "$Label-tree.txt") -Encoding utf8
    } catch { Write-Output "Automation tree dump failed: $($_.Exception.Message)" }
}

# Screen readers announce controls by their accessible name; every visible field, button and link must have one.
function Test-AccessibleNames($Window, [string]$Label, [string]$Screen, [int]$Minimum) {
    $types = 'Edit', 'Button', 'Hyperlink', 'CheckBox', 'ComboBox', 'RadioButton' | ForEach-Object {
        [System.Windows.Automation.PropertyCondition]::new($Ui::ControlTypeProperty, [System.Windows.Automation.ControlType]::$_)
    }
    $interactive = [System.Windows.Automation.OrCondition]::new([System.Windows.Automation.Condition[]]$types)
    $controls = @($Window.FindAll($Scope::Descendants, $interactive) | Where-Object { !$_.Current.IsOffscreen })
    $unnamed = @($controls | Where-Object { [string]::IsNullOrWhiteSpace($_.Current.Name) } |
        ForEach-Object { "$($_.Current.ControlType.ProgrammaticName) $($_.Current.AutomationId) $($_.Current.ClassName)".Trim() })
    Record "$Label`: $Screen controls found" ($controls.Count -ge $Minimum) "$($controls.Count) visible controls"
    Record "$Label`: $Screen controls have accessible names" ($unnamed.Count -eq 0) $(if ($unnamed.Count) { 'Unnamed: ' + ($unnamed -join '; ') } else { 'All named' })
}

# Sign in (or create the owner on first launch) with the keyboard only: Tab to each field, type, Enter.
function Enter-Credentials($Window, $Process, [string]$Label, [bool]$FirstRun) {
    [void][AcceptanceWindow]::SetForegroundWindow($Process.MainWindowHandle)
    try { $Window.SetFocus() } catch { }
    Start-Sleep -Milliseconds 500
    # The page inside the window belongs to WebView2's own process, so compare the foreground window, not the focused element's process.
    Record "$Label`: window is in front" ([AcceptanceWindow]::GetForegroundWindow() -eq $Process.MainWindowHandle) "Focused: '$(Get-FocusedName)'"
    $presses = 0
    while ($presses -lt 25 -and (Get-FocusedName) -ne 'Email') { Send-Keys '{TAB}'; $presses++ }
    Record "$Label`: Email reachable with Tab" ((Get-FocusedName) -eq 'Email') "$presses presses"
    Send-Text $email
    $typed = $Ui::FocusedElement.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Record "$Label`: typing reaches the Email field" ($typed -eq $email) $typed
    Send-Keys '{TAB}'
    Record "$Label`: Tab moves to Password" ((Get-FocusedName) -eq 'Password') "Focused: '$(Get-FocusedName)'"
    Send-Text $password
    if ($FirstRun) {
        Send-Keys '{TAB}'
        Record "$Label`: Tab moves to Confirm password" ((Get-FocusedName) -eq 'Confirm password') "Focused: '$(Get-FocusedName)'"
        Send-Text $password
    }
    Send-Keys '{ENTER}'
    $signOut = Find-Named $Window 'Sign out' ([System.Windows.Automation.ControlType]::Button) 60
    Record "$Label`: $(if ($FirstRun) { 'owner account created and signed in' } else { 'signed in' })" ($null -ne $signOut) 'Workspace shows Sign out'
}

# Windows display scaling and small screens leave the window fewer pixels. Shrink the real window to its minimum (800 x 600) and
# check that the navigation, Sign out and the page heading still fit across it.
function Test-Layout($Window, [string]$Label) {
    $transform = $Window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    $original = $Window.Current.BoundingRectangle
    foreach ($size in @(@(800, 600), @(1024, 700))) {
        $transform.Resize($size[0], $size[1])
        Start-Sleep -Milliseconds 1500
        $frame = $Window.Current.BoundingRectangle
        $parts = @(
            @('Main navigation', $null),
            @('Sign out', [System.Windows.Automation.ControlType]::Button),
            @('Overview', $null)
        ) | ForEach-Object {
            $element = Find-Named $Window $_[0] $_[1] 5
            $r = if ($element) { $element.Current.BoundingRectangle } else { [System.Windows.Rect]::Empty }
            [pscustomobject]@{ name = $_[0]; fits = ($element -and !$r.IsEmpty -and $r.Left -ge $frame.Left - 1 -and $r.Right -le $frame.Right + 1) }
        }
        Save-WindowShot $Window "$Label-layout-$($size[0])x$($size[1])"
        $outside = @($parts | Where-Object { !$_.fits } | ForEach-Object { $_.name })
        Record "$Label`: layout at $([int]$frame.Width) x $([int]$frame.Height)" ($outside.Count -eq 0) $(if ($outside.Count) { 'Cut off or missing: ' + ($outside -join ', ') } else { 'Navigation, Sign out and heading fit' })
    }
    $transform.Resize($original.Width, $original.Height)
}

# Launch the program, work in its window, then close it the way a user does and require a clean exit.
function Invoke-Session([string]$Step, [string]$Label) {
    $process = Start-Process -FilePath $exe -WorkingDirectory $appDir -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    do { Start-Sleep -Milliseconds 500; $process.Refresh() } while (!$process.HasExited -and ($process.MainWindowHandle -eq 0 -or !$process.MainWindowTitle) -and [DateTime]::UtcNow -lt $deadline)
    Record "$Label`: window opens" (!$process.HasExited -and $process.MainWindowHandle -ne 0) "Title '$($process.MainWindowTitle)'"
    Record "$Label`: window title" ($process.MainWindowTitle -eq 'Rokela Business Admin') $process.MainWindowTitle
    $window = $Ui::FromHandle($process.MainWindowHandle)
    try {
        $firstRun = $Step -eq 'setup'
        $heading = if ($firstRun) { 'Set up your business.' } else { 'Welcome back.' }
        Record "$Label`: '$heading' screen" ($null -ne (Find-Named $window $heading -Seconds 120)) 'Shown in the window'
        Save-WindowShot $window "$Label-start"
        if ($Step -ne 'signin') { Test-AccessibleNames $window $Label $(if ($firstRun) { 'owner setup' } else { 'sign-in' }) 3 }
        Enter-Credentials $window $process $Label $firstRun
        Save-WindowShot $window "$Label-workspace"
        if ($Step -eq 'inspect') {
            Test-AccessibleNames $window $Label 'workspace' 5
            Test-Layout $window $Label
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
    notCovered = @(
        'WebView2 was already present on the test machine, so the bundled WebView2 installer was not exercised.',
        'Accessible names were read through Windows UI Automation, which screen readers use; nobody has tried the program with a screen reader yet, and visible keyboard focus was not checked.',
        'Layout was checked at the minimum window size on a small screen; Windows display-scaling settings were not changed.',
        'Physical printers, scanners, cash drawers and cheque alignment need a person and the hardware.',
        'Network outages between two branch computers need two machines.'
    )
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $out 'acceptance.json') -Encoding utf8
$lines = @("## Clean-machine acceptance: $(if ($failed) { 'FAILED' } else { 'passed' })", '',
    "Tested on a fresh $($report.machine) runner$(if ($PreviousVersion) { ", upgrading from $PreviousVersion" }).", '')
$lines += $checks | ForEach-Object { "- $(if ($_.passed) { '✅' } else { '❌' }) $($_.name)" }
$lines += '', '**Not covered by this automated check:**', ''
$lines += $report.notCovered | ForEach-Object { "- $_" }
$lines | Set-Content -LiteralPath (Join-Path $out 'acceptance.md') -Encoding utf8
if ($failed) { exit 1 }
Write-Output "PASS: $($checks.Count) clean-machine acceptance checks."
