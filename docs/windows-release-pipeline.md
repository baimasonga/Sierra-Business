# Windows release pipeline — November 2026

Releases of the Windows installer are now built, checked on a clean Windows machine and published by GitHub Actions
(`.github/workflows/windows-package.yml`). Nothing is built on a developer's computer, and every published installer comes with its
test results and acceptance report.

## Why

- The 16 October package run built 0.33.0 but uploaded `SierraBusiness-Setup-x64.exe`, a name the installer no longer has. Only
  the checksum file was kept, so no installer containing the 16 October work existed.
- Uploads expired after 14 days, and clean-machine install, upgrade and uninstall had never been checked.

## Publishing a release

1. Merge the work into `main`.
2. Push a tag such as `v0.33.0` on a `main` commit, or run **Windows desktop package** by hand with that version.
3. The workflow then:
   1. refuses a version that is already released or older than the newest release;
   2. runs the full test suite, the offline OCR check, the native build and the installer compilation (`scripts/package-windows.ps1`);
   3. installs the result on a fresh Windows runner and checks it (below);
   4. only then publishes the GitHub release **Rokela Business Admin X.Y.Z**. It contains the installer, `SHA256SUMS.txt`, the test
      results (`test-results.trx`) and `acceptance.json`, and the release notes repeat the acceptance summary.

Pull requests that change packaging run the same build and check as version 0.0.0, which is never published.

The installer is not code-signed yet. Set `SIERRA_SIGN_PFX` and `SIERRA_SIGN_PFX_PASSWORD` (or `SIERRA_SIGN_THUMBPRINT`) as
repository secrets passed to the package step once a publisher certificate is bought; until then Windows SmartScreen warns on
install.

## Clean-machine acceptance

`scripts/accept-windows-install.ps1` runs on a new GitHub Windows runner, which is a clean machine. It refuses to run where the
program or its data already exist.

| Stage | Checked |
| --- | --- |
| Install | installer checksum; silent install succeeds; program version; Apps & features entry; Start menu shortcut |
| First launch | window opens with the right title; the page is drawn (colour variety of a screenshot: 562 colours for the setup screen, 1 for a blank window), also at the 800 × 600 minimum window size; company database created; the program closes cleanly when its window is closed |
| Restart | opens and draws again; closes cleanly |
| Upgrade | from the previous GitHub release when there is one, otherwise a reinstall over itself; new version registered; company database kept |
| Uninstall | program, Apps & features entry and shortcut removed; company database kept |

Screenshots of each window are attached to the workflow run.

### What it does not check

- **Signing in and typing into screens.** On the runner, the embedded page did not appear in the window's Windows UI Automation tree:
  only the title bar did. The script records this as a finding instead of failing. Screen readers also use UI Automation, so a
  person should check **Narrator** on a real Windows 10/11 machine before relying on the program for screen-reader users.
- **Keyboard order and visible focus.**
- **Windows display-scaling settings.** The window is checked at its minimum size, and the runner's screen is 1024 × 768.
- **The bundled WebView2 installer.** WebView2 is already present on the runners.
- **Hardware and networks:** printers, scanners, cash drawers, cheque alignment and two-computer network outages.

## Found while building this

- **"Database is locked" during backups.**
  - **Symptom:** a sign-in or posting that committed while a backup was reading the live database failed at once.
  - **Cause:** desktop SQLite connections set no busy timeout.
  - **Fix:** every desktop connection, including the backup's own source connection, now waits up to 30 seconds (`SqliteBusyTimeout`).
  - **Test:** `SignInsDuringABackupWaitInsteadOfFailing` reproduces it. It failed 3 of 3 runs before the fix.
- **Debugging port.** WebView2 ignored remote-debugging arguments given through both the environment and the per-program
  policy. No debugging port was added to the program, because any local process could then read the signed-in workspace.

## Live database tests

The SQL Server and PostgreSQL head-office tests used to be skipped unless a server was configured. CI now runs them on every push,
against throwaway SQL Server 2022 and PostgreSQL 16 containers (`live-databases` job in `.github/workflows/build.yml`). The job fails
if any of them is skipped.
