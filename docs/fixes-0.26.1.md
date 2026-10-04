# Fix release 0.26.1 — app left running blocks updates and uninstall

## What happened
On the build PC the Setup logs (`%TEMP%\Setup Log 2026-10-04 #003.txt`) show the following sequence.
- **The app was still running.** 0.21.0 was installed at 11:56 and opened (it upgraded the company database correctly). At 12:06 the 0.23.0 installer found `SierraBusiness.Maui` still running.
- **Windows could not close it.** The installer's automatic close request failed twice: *"Some applications could not be shut down"*.
- **The update was rolled back.** The installer could not replace `coreclr.dll`, which was in use (*Access is denied*). It was cancelled and rolled back, so the program folder stayed at 0.21.0.
- **The uninstall left files behind.** Uninstalling while that copy still ran could not remove the files it held. The embedded browser's cache was also kept beside the program (`SierraBusiness.Maui.exe.WebView2`), which uninstall never removed.
- **A second copy looks frozen.** Opening the app again while the old copy still holds the company database and browser profile can look like a freeze on opening.

The business data is intact:
- The company database is at the 0.21.0 schema with its guards in place, and nothing is half-written.
- A copy was upgraded to 0.26 in testing in 4.3 s and passed the integrity check.

## Fixes
- **Single instance per company.** A second copy of the same company now shows a message and exits, instead of waiting behind the first. The message says how to end a copy that is not responding.
- **The process really ends.** When the window closes, anything still running is ended after 8 seconds.
- **Browser cache moved.** It now lives with the user's data (`%LOCALAPPDATA%\SierraBusiness\WebView2\<company>`), not in the program folder, so program files can always be replaced. Uninstall removes the old cache folder beside the program; business data and backups are untouched.
- **Setup and uninstall check for a running copy first.** They offer to:
  - continue after you close it;
  - end it now (for a copy that is not responding);
  - cancel.

## To recover on this PC now
1. End **SierraBusiness.Maui** in Task Manager, or restart Windows.
2. Run the 0.26.1 installer. It replaces the old program files and upgrades the database on first start; a before-upgrade backup is taken automatically.
