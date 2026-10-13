; Compile only through scripts/package-windows.ps1 after a successful native publish.
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef WebView2Installer
  #error WebView2Installer is required
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{FCDE1608-8326-49E7-855D-35ED950294CC}
AppName=Rokela Business Admin
AppVersion={#AppVersion}
AppPublisher=Rokela Business Admin
DefaultDirName={localappdata}\Programs\Rokela Business Admin
DefaultGroupName=Rokela Business Admin
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=RokelaBusinessAdmin-Setup-x64
UsePreviousGroup=no
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=rokela.ico
UninstallDisplayIcon={app}\SierraBusiness.Maui.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WebView2Installer}"; DestDir: "{tmp}"; DestName: "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Flags: deleteafterinstall

[Icons]
Name: "{group}\Rokela Business Admin"; Filename: "{app}\SierraBusiness.Maui.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall Rokela Business Admin"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Rokela Business Admin"; Filename: "{app}\SierraBusiness.Maui.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[UninstallDelete]
; The embedded browser's cache that versions before 0.26.1 kept beside the program. Business data and backups live elsewhere and are kept.
Type: filesandordirs; Name: "{app}\SierraBusiness.Maui.exe.WebView2"

[InstallDelete]
Type: files; Name: "{autodesktop}\Sierra Business.lnk"
Type: files; Name: "{userprograms}\Sierra Business\Sierra Business.lnk"
Type: files; Name: "{userprograms}\Sierra Business\Uninstall Sierra Business.lnk"

[Run]
Filename: "{app}\SierraBusiness.Maui.exe"; Description: "Open Rokela Business Admin"; Flags: nowait postinstall skipifsilent

[Code]
// 0.26.1: Setup and uninstall cannot replace or remove program files while Rokela Business Admin is still running, including a copy that has
// stopped responding (Windows' own close request does not reach a frozen window). Find it and, with the user's agreement, end it.
function AppRunning(): Boolean;
var Code: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq SierraBusiness.Maui.exe" /NH | find /I "SierraBusiness.Maui.exe" >nul', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

function EnsureAppClosed(): Boolean;
var Code: Integer;
begin
  Result := True;
  while AppRunning() do
  begin
    case MsgBox('Rokela Business Admin is still running.' + #13#10#13#10 +
                'Yes: close it yourself first, then continue.' + #13#10 +
                'No: end it now (use this if it is not responding; posted records are safe, unsaved typing is lost).' + #13#10 +
                'Cancel: stop.', mbConfirmation, MB_YESNOCANCEL) of
      IDNO:
        begin
          Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM SierraBusiness.Maui.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
          Sleep(2000);
        end;
      IDCANCEL:
        begin
          Result := False;
          exit;
        end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := EnsureAppClosed();
end;

function InitializeUninstall(): Boolean;
begin
  Result := EnsureAppClosed();
end;

function HasWebView2(): Boolean;
var Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if (CurStep = ssPostInstall) and (not HasWebView2()) then
  begin
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      RaiseException('Windows could not start the included WebView2 runtime installer. Run Rokela Business Admin Setup again.');
    if (Code <> 0) and (Code <> 3010) then
      RaiseException(Format('WebView2 installation failed (code %d). Restart Windows and run Rokela Business Admin Setup again.', [Code]));
    if not HasWebView2() then
      RaiseException('WebView2 is not available yet. Restart Windows and run Rokela Business Admin Setup again.');
  end;
end;

// Business data and backups live in the user's data folder, outside {app}, and intentionally survive uninstall.
