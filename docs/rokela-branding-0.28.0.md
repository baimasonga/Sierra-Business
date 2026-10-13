# Rokela Business Admin branding

The application is now Rokela Business Admin. The supplied logo had a baked-in
checkerboard; transparent logo and icon variants were prepared from it with
image editing. The UI uses navy #08275c, blue #075bc2 and green #39db80.

Updated desktop title, login, sidebar, footer, browser titles, product metadata,
app icon, user-facing messages and export names. The installer uses the new name
and creates Rokela shortcuts. Legacy shortcut files are removed during upgrade.
The existing Inno AppId, executable identity, database and WebView2 data locations
are retained for upgrade compatibility and access to existing records.

Windows Release build passed with zero warnings or errors. A production-CSS
login preview at 800 x 600 showed the logo loaded and the 430-pixel panel centered
at x=185, y=20.48, with no scrolling. Screenshot: rokela-login-preview.jpg.
Native Windows launch and upgrade interaction are not independently verified.

Installer: `artifacts/windows/0.28.0/RokelaBusinessAdmin-Setup-x64.exe`,
289,485,450 bytes, product name Rokela Business Admin, version 0.28.0. Full
packaging passed 289 tests with zero failures and six live-provider skips, OCR,
native publish and Inno compilation. Packaged branding assets and CSS match the
source hashes. Installer SHA-256:
`F6F6F38AD773DC6ACCF359875B14F614EA1BB8E82A54D416D76755FD8AC37A3F`.
The installer is unsigned and has not been installed during this run.
