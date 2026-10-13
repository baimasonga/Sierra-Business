# Latest work review and release 0.29.1

Reviewed on 8 October 2026. Git HEAD remains f39323b (0.27.0); later branding, form feedback, centering, sync security, recovery and navigation work is present as uncommitted source changes. No reset or commit was performed. The Windows feature comparison is a report, not implementation of its proposed backlog.

## Review finding and fix

Recalling a held sale called MayLeave, which cleared automatic recovery ownership. The recalled sale remained in the held-sale register, but subsequent edits no longer received an automatic recovery copy. Recall now restores recovery ownership after loading the current held sale. A rendered-workspace regression verifies the recalled held-sale identity, quantity and subsequent customer edits in the recovery copy.

The new Go to a screen search uses permission-filtered navigation, multi-term name/area matching, Enter to open, Escape to clear and Alt+G to focus. Compact navigation opens before shortcut focus. Results use the existing navigation path, preserving unfinished-document and pending recovery guards.

## Verification

The targeted held-sale regression passed. Full packaging validation completed successfully: 298 passed, zero failed, six live-provider skips (304 total). Offline OCR passed PDF/PNG, classification, cancellation and oversized-document rejection with a 94% fixture score. Self-contained Windows publish and Inno Setup compilation succeeded. `git diff --check` passed.

Installer: artifacts/windows/0.29.1/RokelaBusinessAdmin-Setup-x64.exe, product version 0.29.1, 289,488,511 bytes. SHA256 independently verified against SHA256SUMS.txt:

A202C7E0FA548A48BBFA8059107C9D1A7242E24473FD43238DB321C6AFADE56D

The installer is unsigned and has not been installed by this review. Logs and test TRX are retained in the release folder. Upgrade head office and branches together when moving from legacy sync clients to the compatibility-aware releases.

## Remaining limits

Native Windows clicks, DPI, physical printers/drawers, clean-machine installation, upgrade/uninstall and actual power interruption are not certified by these automated tests. Live SQL Server/PostgreSQL checks require configured test instances. The feature-comparison backlog remains open; Android and manufacturing are excluded in that latest comparison scope.
