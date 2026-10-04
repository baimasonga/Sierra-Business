# Production readiness — 0.26.0

This release prepares Sierra Business for real deployment. Three things still need you; they are listed in [acceptance checklists](acceptance-checklists.md):
- a code-signing certificate;
- the real counter hardware;
- a clean Windows machine.

## Upgrade and rollback
- **Upgrading.** When a new version opens an existing company, it first writes a `before-upgrade-….db` backup, then upgrades the database. It also re-applies the history guards and re-seals.
- **Rollback rehearsal.** `ProductionReadinessTests.UpgradeKeepsAPreUpgradeBackupThatTheOlderReleaseCanRestore` checks the whole rollback path:
  1. A 0.25.0 database is upgraded; the backup is taken and the history verifies.
  2. New work is posted.
  3. The older release's migration list **accepts** the pre-upgrade backup and **refuses** the upgraded database ("made by a newer version").
  4. The backup is still at the old schema.
  5. Restoring it brings the pre-upgrade data back without the later work.
- **To roll back on a computer:**
  1. Uninstall the new version (your data folder is kept).
  2. Install the previous installer.
  3. Under **Backups & integrity**, restore the `before-upgrade` backup.

  Anything recorded after the upgrade must then be entered again, or kept by staying on the new version.
- **Head office (SQL Server).** Take a SQL Server backup before `--migrate`. Roll back by restoring that backup and redeploying the previous build.

## Update check
Once a day, and on **System health → Updates → Check now**, the app reads a release feed:
```json
{ "version": "0.26.0", "url": "https://…/SierraBusiness-Setup-x64.exe", "sha256": "<64 hex characters>", "notes": "What changed" }
```
- **Where it is set.** On the server, in `Updates:Feed`. On a computer, in `update-settings.json` (`{"Feed": "https://…"}`) in the company data folder. Blank means checks are off; that is the default.
- **Validation.** The feed and the download must be https. The version and the SHA-256 must be well formed. A bad feed is reported and never replaces the last good answer.
- **Nothing installs by itself.** The app only says which version is available, where to get it, and the SHA-256 to compare before installing.
- **Version numbers.** Builds now stamp the release number into the application, so System health shows the real version.

## Code signing
`scripts/package-windows.ps1` signs the application and the installer with SHA-256 and an RFC 3161 timestamp, then verifies both signatures, when either of these is set:
- `SIERRA_SIGN_THUMBPRINT` (a certificate in the Windows certificate store), or
- `SIERRA_SIGN_PFX` with `SIERRA_SIGN_PFX_PASSWORD`.

`SIERRA_SIGN_TIMESTAMP` changes the timestamp server. Without these, the build says it is unsigned and carries on as before.

## Performance
`ProductionReadinessTests.HalfAYearOfTradingStaysFast` builds six months of a busy branch: 40 products and 600 two-line invoices. It then checks each measure against a limit:
- posting averages under 1 s per sale;
- each report runs in seconds.

The limits are generous so slow build machines pass; the timings are printed.

| Measure | Development container | Windows build PC (whole suite running in parallel) |
|---|---|---|
| Posting a sale (with sync queueing) | about 27 ms | about 530 ms |
| Dashboard for a year | 0.5 s | 4.1 s |
| Sales by month or customer, first day-book page | under 0.05 s each | under 0.1 s each |
| Stock valuation, ageing, balance sheet, cash flow | under 0.05 s each | 0.3–0.4 s each |
| Excel export of the day book | 0.2 s | 0.2 s |
| Full integrity verification | 0.3 s | 10 s |
| First sync of all history to head office | 4.3 s (1,200 invoices: 7.8 s, against 48.6 s before this release) | 129 s |

**Windows is much slower at writing.** On the Windows build PC, writing (posting, verification, first sync) was 10 to 30 times slower than in the container. The whole suite was running in parallel at the time. Reads stayed fast.
- **Likely causes.** The database uses SQLite's default rollback journal, which flushes the disk several times per commit. Antivirus also scans the database files.
- **Still within limits.** A sale still posts in about half a second, and every limit passed.
- **Recommended next step.** Switch the desktop database to WAL journaling. This should be measured on the counter PC first; see the hardware checklist. Meanwhile, excluding the Sierra Business data folder from antivirus real-time scanning is the quickest improvement.

**Faster sync.** Applying a sync batch now happens in one transaction: the rows it touches are loaded in a few queries rather than one per row. If anything in the batch fails, it is applied one commit at a time so the faulty commit is recorded as a conflict.

## Security review
| Area | Finding | Action |
|---|---|---|
| Web headers | No browser hardening headers | Added Content-Security-Policy (self only; no inline or remote script), X-Frame-Options DENY, frame-ancestors none, nosniff, no-referrer and Permissions-Policy, plus HSTS outside development. Checked in Chromium: sign-in and the main screens load with no policy violations. |
| Sign-in | Lockout after 5 failures, but unlimited attempts from one address across accounts | 10 sign-in attempts a minute per address, then HTTP 429. Checked: the 10th and later attempts are refused. |
| Sync endpoints | Key per branch, compared in constant time; https enforced at the branch | Added 240 calls a minute per branch and address. Requests are limited to 20,000 changes. A branch cannot write another branch's records. Keys are never logged or put in addresses; error text is cleaned of keys, tokens and passwords. |
| Posted history | Database guards on SQLite | The same guards on SQL Server (triggers), verified by test. |
| Sign-in page | Message text | Fixed text only, never echoed from the request. |
| Update feed | Could point anywhere | https only, validated fields, never auto-installed. |
| Desktop sync key | Stored in `sync-settings.json` in the user's own profile | Accepted for now: the folder is private to the Windows account. Windows DPAPI encryption is recommended once the signing work is done. |
| Packages | Vulnerable transitive `System.Security.Cryptography.Xml` | Pinned to 10.0.12 in every project that pulls it in (the build treats audit warnings as errors). |

## Windows test fix
The live-refresh dashboard test was skipped on Windows from 0.19.0. The test was reading the rendered page from its own thread while a background refresh was rendering. It now reads the page on the renderer's dispatcher and runs on Windows again.

280 tests pass (SQL Server available).
