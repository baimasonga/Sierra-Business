# Windows release acceptance — 0.31.0

## Checks completed on 9 October 2026

`scripts/verify-windows-release.ps1 -Version 0.31.0` passed 26 artifact checks: installer checksum and product/version, unique publish folder, native application version, bundled .NET runtime, branding, form/print/CSS assets matching the immutable source snapshot, offline OCR dependencies/model checksum, absence of company databases/credential files, all 423 source-manifest hashes, and successful release-test evidence.

Machine-readable results: `artifacts/windows/0.31.0/release-verification.json`. The release suite recorded 318 passed, zero failed and six skipped live-provider checks. The installer is unsigned.

Windows computer-use initialization failed twice with `failed to write kernel assets: The system cannot find the path specified. (os error 3)`. No native UI actions, installation, authentication or test postings were attempted. Existing company records and the installed application were not changed. Artifact checks do not establish native acceptance.

## Subsequent native retry

After restarting the automation session, the runtime initialized successfully. The existing installed application launched; its executable reports version 0.31.0. The login card and Rokela branding appeared centered. The user completed authentication manually.

On the signed-in workspace, Overview → New sale opened correctly. Page Down reached the invoice form. Clicking Product opened the searchable picker with catalogue products including A3-80G and A4-80G; closing returned to the invoice. Clicking Business date opened the October 2026 calendar with day 9 selected; closing returned to the invoice without changing the date.

The current company was Index Limited Sierra Leone. Overview showed 74 products and zero available stock lots. No line was added and no transaction was posted. Test posting, recovery, restart, printer and clean-install acceptance remain pending.

After sending F3, screenshot capture failed with `encode latest capture frame failed: window crop is outside captured monitor`. Refreshing the returned window and retrying capture produced the same error. The shortcut result is therefore unverified; this capture failure is not evidence of an application error. No stale coordinates or further UI actions were used.

## Reopened-window checks and shortcut defect

The reopened signed-in window could be captured again. New sale and Receive stock tabs responded, and F2 returned from Receive stock to New sale. After F3, accessibility inspection confirmed Receive stock **and** an unwanted WebView Find on page panel. This establishes a native shortcut defect independent of the intermittent screenshot failure.

Root cause: `InvokeVoidAsync` received a covariant string array as its `params object[]` arguments, so JavaScript received separate string arguments rather than the intended array. `ShortcutSettings.razor` now casts the array to `object`, preserving a single array argument. `WorkspaceHeaderTests` asserts the exact argument count and configured key array. This correction is in source only; the existing 0.31.0 installer has not been replaced or modified. Native revalidation needs a package containing the correction.

The current company still had no available stock, no line was added and no transaction was posted. The observed layout allowed horizontal tab wrapping and vertical form scrolling. This is partial laptop-screen evidence, not a complete DPI/accessibility acceptance result.

## Next native acceptance session

Use a separate acceptance company with synthetic products and parties. Keep the existing company untouched. Record the installed app version, Windows version and display scaling before starting.

| Check | Expected result | Current result |
|---|---|---|
| Install 0.31.0 on a clean offline Windows x64 machine | Bundled runtime permits launch; product/version/branding correct | Pending |
| Receive 10 quantity units at cost 60, then sell 2 at 100 cash | Product/date controls respond; adding a line enables posting; one invoice; quantity 8; sales 200; gross profit 80 before configured tax | Pending native execution; equivalent HTTP scenario passed |
| Repeated click or uncertain-result retry | No duplicate invoice; register and totals remain correct | Pending native execution; service/API idempotency covered |
| Restart with saved transactions | Register, stock, journal and integrity check retain the committed result | Pending native execution |
| Restart with unfinished sale, quotation and tender edits | Recovery offer appears; restoring retains draft; no duplicate posting | Pending native execution; encrypted-store/component scenarios covered |
| Conflicting tender revision | Restoring older edited bid refuses to overwrite newer version | Pending native execution; component scenario covered |
| Print invoice/receipt and retry with unavailable printer | Preview retains posted amounts; failures do not lose transactions; output fits target paper | Pending printer/device execution |
| Install next version and rehearse rollback | Pre-upgrade backup, intact history, safe older-release backup restore | Pending installer execution; desktop-store rehearsal covered |
| Uninstall acceptance copy | Program removed; company data and backups retained | Pending |

Do not perform live power interruption, uninstall, or rollback against the real company as part of this checklist. Use the acceptance machine/company and retain its backups. Physical printers, scanners, drawers, two-device sync and signing/distribution acceptance remain separate from the development-machine artifact checks.
