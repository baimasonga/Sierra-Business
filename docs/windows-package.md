# Windows application and single-package delivery

Latest preview: **0.14.0**, `artifacts\windows\0.14.0\SierraBusiness-Setup-x64.exe`. See [equipment notes](equipment-installation-warranty-0.14.0.md). See [supplier invoice notes](supplier-invoices-0.13.0.md). See [purchasing notes](purchasing-imports-0.12.0.md). See [Excel import notes](excel-import-0.11.0.md). See [records-safety notes](records-safety-0.10.2.md). See [serial-count instructions](serial-counts-0.9.0.md). See [earliest-expiry sales instructions](earliest-expiry-0.8.0.md). See [batch-count instructions](batch-counts-0.7.0.md). See [counter-sales instructions](counter-sales-0.6.0.md). See [banking and cash-control instructions](banking-cash-control-0.5.0.md). See [negotiated-sales instructions](negotiated-sales-0.4.0.md). An authorized manager must configure confidential minimum prices in Sales controls before quoting/selling products after the update. See [accounting improvements](accounting-improvements-0.3.1.md). See [new business workflows](business-workflows-0.3.0.md) and [remaining implementation scope](implementation-progress.md). The 126 integration tests, Windows OCR smoke check, MAUI publish and installer compilation passed. Native company switching/printing and clean-machine acceptance remain outstanding. See [document capture](document-capture.md) for offline scanning/OCR and optional online assistance, and [bug-fix notes](bugfix-release-0.1.1.md) for the earlier audit fixes and desktop configuration instructions. Close Sierra Business before running the update installer. Business data stays in the per-user Data directory; make a backup before updating. Earlier build evidence below is retained as history.

Release 0.2.0 includes English Tesseract data, x64 OCR libraries and app-local Microsoft Visual C++ runtime libraries. The packaging gate runs 49 integration tests and the real Windows PDF/image OCR smoke runner, verifies the model checksum and checks the capture stylesheet is present. Originals and revision history are included in the existing SQLite backup. Physical WIA scanner compatibility, paid AI calls and clean-machine install/upgrade remain unverified.

Handoff check on 2026-09-27: the 0.1.1 installer checksum matches, all 40 .NET 10 tests pass, and the native application's embedded sign-in screen was visually verified. The installer is 275,779,915 bytes. See the latest section of [verification](verification.md) for the checks and remaining limits.

User decision: retain **.NET 10 + .NET MAUI Blazor Hybrid**. Do not ship the .NET 8 diagnostic copy as the desktop product.

## Intended user experience

1. Double-click **SierraBusiness-Setup-x64.exe**.
2. Complete the Windows installation wizard; optionally create a desktop shortcut.
3. Open **Sierra Business** from the Start menu or desktop.
4. On first launch, create the local owner account within the application.
5. Work in the native application window. No terminal, browser tab, SDK installation or manual web server is required on the destination PC.

The single delivery file is an **installer**. Like normal Windows software, it installs an executable and its supporting files. The build includes the .NET and Windows App SDK runtimes plus the official standalone WebView2 prerequisite. This avoids relying on download access during installation. The installer targets Windows 10 version 1809 or newer, x64-compatible systems.

## Source prepared in this change

- `SierraBusiness.Maui`: native Windows window with BlazorWebView, shared workspace and bundled CSS/icon.
- `SierraBusiness.Desktop`: local Identity setup/sign-in, password lockout, eight-hour in-memory sessions, permission/security-stamp revalidation per operation, database initialization and exclusive per-user application lock.
- SQLite storage in `%LOCALAPPDATA%\SierraBusiness\Data\sierra.db`, independent of the installation folder. Backups are taken through SQLite's backup API before a pending migration is applied to an existing database.
- An upgrade failure stops startup; pending-model checks remain enabled. No failed migration is bypassed.
- Inno Setup per-user installer with shortcuts, uninstall registration and embedded WebView2 installer. Uninstall does not delete the data directory or backups.
- Build script that refuses to package failed builds/tests or local databases/credentials.

## Current package status — 2026-09-27

The native MAUI app and single-file installer have now compiled successfully.

- Installer: `artifacts\windows\0.1.0\SierraBusiness-Setup-x64.exe` (275,776,103 bytes).
- Checksum: adjacent `SHA256SUMS.txt`, independently verified after compilation.
- All 23 integration tests passed in the packaging run on .NET 10.
- Native executable remained responsive with window title Sierra Business, produced no stderr output, and initialized its local SQLite database. No owner or sample business records were created by the launch check.
- The installer includes self-contained runtimes and the Microsoft-signed offline WebView2 prerequisite. The Sierra Business installer itself is not code-signed.
- WindowsAppSDK is pinned to 1.7.250909003. Optional ReadyToRun optimization is disabled to avoid a missing crossgen runtime pack; self-contained runtime deployment remains enabled.

Double-click the installer, finish the wizard, then open Sierra Business and create the owner account. This is a development preview of the implemented modules. Full UI interaction, clean-machine offline installation, upgrade and uninstall testing remain required before production use. The launch check does not establish that every screen works.

## Build prerequisites

On the build computer:

1. .NET 10 SDK and Windows MAUI build prerequisites (including the Windows SDK).
2. `dotnet workload install maui-windows` using the .NET 10 SDK.
3. Inno Setup 6.
4. Microsoft's **Evergreen Standalone Installer, x64**, for WebView2. The small online bootstrapper is not suitable for this offline package. The packaging script checks size and Microsoft Authenticode signature.

Check without publishing:

```powershell
.\scripts\package-windows.ps1 -Check -WebView2Installer 'C:\Downloads\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
```

Build the single installer:

```powershell
.\scripts\package-windows.ps1 -WebView2Installer 'C:\Downloads\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
```

Expected output after a successful build:

`artifacts\windows\0.1.0\SierraBusiness-Setup-x64.exe`

The script also writes a SHA-256 checksum. No signing certificate has been configured; signing is a separate release step once a publisher certificate is available.

## Verification gates before handing over an executable

- Build and test under .NET 10; resolve any EF Core 10/Identity 10 migration-model differences noted in `verification.md`.
- Compile the actual MAUI project and Inno script. Static source review does not prove these compile.
- Fresh VM installation without .NET, Windows App SDK or WebView2 preinstalled, with networking disconnected.
- First-run owner creation, rejected weak/mismatched passwords, sign-in and lockout.
- Create catalogue data, receive stock, sell offline, close/reopen and verify persisted stock/ledger/audit.
- Expired/revoked session rejects writes; sign-out clears the workspace.
- Second instance cannot concurrently initialize the same database.
- Upgrade preserves records and produces a consistent backup; failed upgrades stop safely.
- Uninstall/reinstall preserves the business database; no sample credentials/data in the installer.

Only the currently implemented business modules are packaged. An installer does not complete tenders, central sync, Excel import or the other outstanding modules.

## Desktop backups

New desktop owners receive `backups.create`. Use **Back up records** in the sidebar to create a consistent SQLite snapshot while the workspace is open. The application rechecks the current session and permission; stale UI permissions do not authorize a backup. Existing development accounts are not automatically granted the new permission.

Backups are saved under `%LOCALAPPDATA%\SierraBusiness\Data\Backups`. Each has a unique timestamped name. SQLite integrity verification must pass before the final `.db` file appears; incomplete `.partial` files are removed. The same verified snapshot routine protects pending database upgrades. A backup contains all local business data and account password hashes: keep it private and copy it to secure external storage to protect against disk failure.

Restore is currently an administrator procedure, not an in-app button. Close Sierra Business, preserve the complete current Data folder elsewhere, and restore a tested backup as `sierra.db` in a fresh Data folder (never overlay a database with old WAL/SHM sidecars present). Start the application and verify login, stock, document totals and trial balance. Retain the original folder until verification succeeds. Automated integration tests restore into an independent folder and check that later live transactions do not appear in the restored snapshot.

## CI build route

The standard verification workflow now compiles the MAUI Windows host after the solution tests. The manually triggered **Windows desktop package** workflow installs .NET 10, MAUI Windows and Inno Setup on a Windows runner, then invokes the same packaging script used locally. Supply a semantic version and Microsoft's current Evergreen Standalone x64 installer HTTPS URL. The downloaded file must pass the script's Microsoft Authenticode and size checks. Inputs are passed as environment variables, not interpolated into shell commands.

On success, download the workflow artifact containing the installer and SHA-256 checksum. This workflow has been authored but has not been run from this workspace; no repository remote is configured. A successful CI build still requires the clean-machine installation checks above before distribution.

## Platform references

- [Microsoft: publish unpackaged MAUI Windows applications](https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/publish-unpackaged-cli?view=net-maui-10.0).
- [Microsoft: WebView2 runtime distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution).
- [Inno Setup: per-user installation privileges](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm).




