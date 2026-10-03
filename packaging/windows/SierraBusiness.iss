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
AppName=Sierra Business
AppVersion={#AppVersion}
AppPublisher=Sierra Business
DefaultDirName={localappdata}\Programs\Sierra Business
DefaultGroupName=Sierra Business
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=SierraBusiness-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
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
Name: "{group}\Sierra Business"; Filename: "{app}\SierraBusiness.Maui.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall Sierra Business"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Sierra Business"; Filename: "{app}\SierraBusiness.Maui.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\SierraBusiness.Maui.exe"; Description: "Open Sierra Business"; Flags: nowait postinstall skipifsilent

[Code]
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
      RaiseException('Windows could not start the included WebView2 runtime installer. Run Sierra Business Setup again.');
    if (Code <> 0) and (Code <> 3010) then
      RaiseException(Format('WebView2 installation failed (code %d). Restart Windows and run Sierra Business Setup again.', [Code]));
    if not HasWebView2() then
      RaiseException('WebView2 is not available yet. Restart Windows and run Sierra Business Setup again.');
  end;
end;

// No UninstallDelete section: business data and backups intentionally survive uninstall.
