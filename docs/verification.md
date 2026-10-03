# Verification record

Latest validation: 2026-10-02. Earlier records below are retained as history.

## Serial stock counts 0.9.0 — 2026-10-02

All 18 targeted inventory/component tests passed. Serial coverage includes valued surplus/shortage, replay, restoration by reversal, duplicate history and other-warehouse rejection, sold-serial rejection, fractional/invalid identity and stale quantity checks, reservation protection, transfer reversal, consumed-surplus reversal rejection and UI identity normalization/reset. Earlier count payload compatibility tests also pass.

Authenticated HTTP checks passed serial-count creation/replay and exact stock identity/value verification alongside the existing workflows. EF reports no pending model changes. Tests use temporary databases. Native visual/scanner and clean-machine acceptance remain outstanding; final packaging results follow below.
## Earliest-expiry batch suggestions 0.8.0 — 2026-09-29

The full release gate passed all 119 tests (zero failures/skips); the targeted inventory and workspace run also passed 17 tests. Offline PDF/image OCR checks passed at 94%. New cases verify deterministic expiry/batch ordering, undated batches last, fractional allocations, expired and reserved stock exclusion, existing draft allocation subtraction, warehouse scope, future sale dates, permission/branch rejection and final-posting stock revalidation. Component-handler tests verify converted draft quantities, agreed-price preservation and failure retaining inputs.

Authenticated HTTP checks passed suggestion creation followed by a real sale and stock verification, alongside the existing business/security workflows. EF reports no pending model changes. Tests use isolated temporary databases. Full native/visual/hardware and clean-machine acceptance remain outstanding. Full release-gate and installer results are recorded below after completion.
## Batch stock counts 0.7.0 — 2026-09-29

All 113 integration/component tests passed with zero failures/skips. New coverage verifies batch/expiry/warehouse isolation, exact lot valuation, replay, reversal, stale counts, reservations, expired-stock disposal/sale rejection, legacy quantity-count payloads, and selection/reset behavior in the shared UI. Authenticated HTTP checks passed count creation/replay and stock value alongside the existing trading, accounting, banking and POS workflows. Offline PDF/image OCR smoke checks passed at 94%. EF reports no pending model changes.

Validation uses isolated temporary databases. Full visual/native and clean-machine acceptance remains outstanding. Installer completion and checksum are recorded below once verified.
## Banking and cash control 0.5.0 — 2026-09-28

101 integration tests passed in the release gate. New tests cover quoted CSV/invalid precision, duplicate statements, signed amount/date matching, cancellation before voiding, safe minimum dates, cheque deposit/clearance/bounce, settlement-before-undo interlocks, stale action versions, postdated cheques, receipt idempotency/overpayment, paid supplier credit/refund/allocation, part-paid return splitting, till ownership/staff attribution, stale closing, immutable counts and permission denial. Earlier accounting/trading/security/migration tests also pass.

Authenticated HTTP checks passed statement import and reviewed matching, automatic cheque registration/deposit/clearance and till shortage capture alongside the existing trading/security flows. EF reports no pending model changes. Windows offline OCR passed PDF/image/classification/cancellation/oversize checks at 94%. MAUI published successfully with executable product version 0.5.0. Tests use isolated temporary databases; the user's live data and running application were not changed. Shared UI builds successfully; complete interactive/native printing/scanner and clean-machine upgrade acceptance remain outstanding.
## Negotiated sales and fulfillment 0.4.0 — 2026-09-28

91 integration tests passed in the first release gate, including confidential floor authorization/non-disclosure, unit-conversion enforcement, missing-policy rejection, immutable policy history, reservation protection/consumption, delivery-to-invoice stock-once posting, clearing-account valuation, delivery/invoice reversal order, paid invoice returns, partial customer-credit refunds/allocations, cross-customer allocation rejection, legacy-return migration and owner-only permission upgrade. Render tests verify a delivery note includes tracking/reversal status without commercial prices. Invoicing previously delivered goods after their expiry date is tested separately from dispatch-time expiry checks.

Authenticated HTTP tests passed reservations, delivery/invoicing, customer credit creation, partial refund and non-manager confidential-price denial, alongside the previous trading/accounting/authentication checks. EF reports no pending model drift. The real Windows OCR smoke test passed at 94% fixture score. Native publishing passed. Final package rebuilding includes the final receipt-view reset and draft-warning fixes; only the final successful build/checksum establishes the handoff artifact. Physical printing, scanner compatibility and comprehensive native UI/clean-machine acceptance remain unverified. Production user data was not migrated or used by these tests.
## Accounting improvements 0.3.1 — 2026-09-28

80 integration tests passed and passed again in the packaging gate. New cases cover cancellation retry idempotency, stale-match rejection, immutable retained history, replacement matching, current permissions/branch access, opening cash, internal transfers, noncash trading and reversal cash flows. Authenticated HTTP tests passed the new history/cancellation/cash-flow endpoints and balance reconciliation. The additive BankMatchCorrections migration has no pending EF model drift. Native MAUI publish and the real Windows OCR smoke test passed. The two new accounting views and their print output still require hands-on native acceptance; these checks used temporary databases only.
## Business workflows preview 0.3.0

77 integration tests passed, including upgrade from the actual 0.2 database schema, old idempotent request replay, accounting, orders, warehouses, payroll, parties, partial returns, account administration, period closing, future tax configuration and printable invoice encoding/historical amounts. The release gate repeated all 77 successfully. EF reports no pending model changes.

The extended HTTP smoke run passed sign-in, authenticated workspace, trading/reversal/replay, module reads, journal voucher, bank statement match, warehouse transfer, user creation, served print helper and antiforgery rejection. The print helper initially exposed an incorrect static-content path in the test host; the helper is now a linked explicit asset in both hosts, and the test host uses its proper content directory.

Offline OCR passed a real two-page PDF and PNG test, classification, cancellation and oversized-document rejection (94% OCR score on its fixture). Native .NET 10 MAUI Release publish succeeded; inspected staging includes the print helper and its HTML reference. Native company switching, print dialogs/output, all new UI workflows and clean-machine install/upgrade remain unverified. All database tests used isolated temporary data; existing signed-in user records were not migrated.


## Document capture release 0.2.0

Final corrected installer compiled successfully: `artifacts/windows/0.2.0/SierraBusiness-Setup-x64.exe`, 281,676,913 bytes. Independently recomputed SHA-256 matches `SHA256SUMS.txt`: `529571A74054274CB6B6858E12DB5D8E7A46BF8F22E8A2693709F7EEA6A60770`. Final staging contains the referenced capture CSS, pinned English model, x64 OCR libraries and Microsoft runtime dependencies.

The packaging gate passes all 49 integration tests and a real Windows OCR smoke run for a two-page PDF, PNG, classification, cancellation and oversized-page rejection. Native MAUI Release build/publish succeeds; EF reports no pending model changes. The installer bundles English OCR, native libraries and app-local Microsoft C++ runtimes. A missing capture stylesheet was found during output inspection, changed to an explicit static asset, and added to the package gate. See [capture behavior and limitations](document-capture.md). Scanner hardware, live AI service behavior, the new screen's complete visual acceptance and clean-machine install/upgrade are not yet verified. Existing signed-in user data was not used for tests or migrated during development.

## Installer handoff verification — 2026-09-27

- Re-ran the main Release build and native MAUI Release build: both succeeded with zero warnings/errors on SDK 10.0.401.
- All 40 integration tests passed again on .NET 10. EF Core's Release model check reports no pending changes.
- Independently verified the existing 0.1.1 installer: 275,779,915 bytes; SHA-256 `865C145666556912141D1633BECBAF4911476D34658E9D98C31B4C521CE20BE2`, matching its checksum file.
- Launched the corresponding published native executable and inspected its actual Windows window and accessibility tree. The embedded, styled sign-in screen renders successfully inside Sierra Business, with email/password controls and no external browser tab. An existing owner account was present; no credentials were entered and no business transactions were created during this check.
- Published runtime configuration includes .NET 10.0.12; native Windows App SDK and SQLite runtime files are present. The offline WebView2 prerequisite's Microsoft signature is valid.
- The app creates WebView2 cache files on launch in its staging directory. These are post-publish runtime files, not evidence of business data in the original installer; the package script uses a fresh staging directory for each build.
- A later read-only window observation showed the signed-in Overview workspace, with catalogue, stock, documents, trial balance and backup controls present. No automated authentication or business write was performed. The app was left open rather than interrupting that signed-in session.
- This verifies native startup and visible sign-in/overview rendering. Clean-machine installation, full transactional UI acceptance, upgrade and uninstall checks remain outstanding. The installer remains an unsigned development preview.

## Bug-fix release 0.1.1

All 40 .NET 10 tests pass, including the new audit-history save-overload, current Identity claims/stamp, configuration-file, net sales/reversal, unsupported tracking-mode, component navigation/price and missing-schema health tests. The packaging gate reran all 40 successfully. EF Core reports no pending model changes.

The real HTTP smoke test `scripts/verify-web.ps1` passed health, sign-in, authenticated workspace rendering, catalogue creation, receipt, cash sale, duplicate replay, reversal, net-sales reconciliation and missing-antiforgery rejection (HTTP 400). It used an isolated temporary database and generated credentials; no existing user database was modified. Component-handler tests do not replace full native visual acceptance. See [release notes](bugfix-release-0.1.1.md).

## Native Windows package completed

MAUI Windows workload 10.0.0 is installed. Native Release publish succeeds after pinning WindowsAppSDK 1.7.250909003 and disabling optional ReadyToRun optimization. The app remains self-contained. The complete packaging script passed all 23 integration tests, published the native host, and compiled the Inno Setup installer successfully.

Output: `artifacts/windows/0.1.0/SierraBusiness-Setup-x64.exe`, 275,776,103 bytes. Its SHA-256 matches the generated `SHA256SUMS.txt`. The installer is unsigned; its embedded WebView2 prerequisite has a valid Microsoft signature.

The published app was launched on this development PC. Its process remained responsive with title Sierra Business, stderr was empty, and it initialized `%LOCALAPPDATA%/SierraBusiness/Data/sierra.db`. No owner credentials or business transactions were created. This is a process/startup check, not a full visual or interaction test. Clean-machine offline installation, UI acceptance, upgrade and uninstall/reinstall remain unverified. Earlier missing-workload and missing-installer statements below are historical and superseded.

## Primary .NET 10 validation after SDK installation

- Installed SDK: **10.0.401**, runtime 10.0.12.
- `dotnet restore SierraBusiness.sln` succeeds with NuGet vulnerability auditing enabled.
- `dotnet build SierraBusiness.sln --no-restore -c Release` succeeds with zero warnings/errors.
- `dotnet test SierraBusiness.sln -c Release --no-restore` passes **23 tests** on .NET 10. Results: `tests/SierraBusiness.IntegrationTests/TestResults/net10-validation.trx`.
- EF Core CLI 10.0.0 `migrations has-pending-model-changes` reports no model changes. Existing migrations apply in the real SQLite tests without suppressing EF's pending-model check; no additive migration is needed for this model.
- Updated SQLitePCLRaw bundle to 2.1.13 and the design tooling's private System.Security.Cryptography.Xml dependency to 10.0.12 to resolve restore audit failures. A regression test checks the actual loaded SQLite native version is at least 3.50.2; migration replay also asserts model/snapshot compatibility.
- `dotnet list SierraBusiness.sln package --vulnerable --include-transitive` reports no known vulnerable packages for any solution project against the current feed. This does not audit the un-restored MAUI host.
- The desktop root Razor components also compile in a separate .NET 10 Razor check project with zero warnings/errors. This verifies their shared-service/callback wiring without claiming a MAUI native build or visual test.
- MAUI workload installation rolled back with a Windows elevation cancellation. The SDK alone does not compile the native host. Run `dotnet workload install maui-windows` from Administrator PowerShell, then rerun the packaging script. No native executable or Setup.exe has been generated.
- Direct native-project build stops at `NETSDK1147` because the MAUI SDK packs are absent; compilation has not started.
- Offline WebView2 x64 download completed with a valid Microsoft Corporation Authenticode signature. The Windows package prerequisite check now reports only the missing MAUI workload; Inno Setup is available.

The .NET 8 diagnostic limitation below is superseded for solution/service tests only. Native MAUI behavior, browser interaction, SQL Server, clean-machine installation and the broader production checks remain unverified.

## Desktop continuation checks

- Added permission-checked manual backups and integrity verification for both manual and pre-upgrade snapshots.
- The isolated .NET 8 diagnostic suite now passes **22 tests**, including the six existing desktop account/session checks and two new backup checks. Backup restoration verifies owner sign-in, 8 remaining units valued at 480, two documents, three pending operations and balanced accounting after restoring into an independent directory. A later sale remains only in the original database. Signed-out and permission-revoked users cannot create backups.
- Native Windows compilation has been added to CI, with a separate manually triggered installer workflow. These workflows have not been executed here.
- The diagnostic solution and a separate Razor compilation of the desktop root both build with zero warnings/errors. This checks the backup callback wiring, not the native MAUI host or visual behavior.
- Retried the official .NET 10 SDK download during continuation; it timed out after 600 seconds, followed by DNS failures. Primary restore/build/test still fail with `NETSDK1045`. The package prerequisite check also reports the missing MAUI workload and offline WebView2 installer. The primary .NET 10 target has not been downgraded. The native executable and installer remain ungenerated.

## Historical: requested .NET 10 target before SDK installation

`dotnet restore`, `dotnet build` and `dotnet test` were attempted on the primary solution. Each is blocked by **NETSDK1045**: the installed SDK is 8.0.425 and cannot target net10.0. Runtime 10.0.12 alone does not provide an SDK. Official installer and direct SDK downloads failed through timeouts, DNS failures and interrupted TLS transfers. Partial downloads are ignored under `.tools`.

No successful .NET 10 build, MAUI build or SQL Server validation is claimed.

## Isolated diagnostic copy

The source was copied into ignored `.tools/compat`, then only that copy was retargeted to .NET 8 / C# 12 using locally cached EF Core 8 packages. The primary source stack was not changed.

- Diagnostic solution restore and build succeeded, with zero compiler warnings/errors.
- All **14** real SQLite integration tests passed. They cover restart persistence, duplicate delivery, conflicting operation reuse, atomic rollback, branch/permission denial, serial uniqueness, expired stock, full-sale reversal, unsafe receipt reversal denial, credit postings/payment validation, unit conversion, closed periods, migration replay, concurrent oversell prevention, and immutable posted records.
- A temporary diagnostic database was initialized from migration and an ephemeral owner created.
- HTTP sign-in succeeded; the authenticated workspace rendered.
- Authenticated API workflow created a product, received 10 units at NLe 60, then sold 2 at NLe 100. Retrying the same sale returned the same ID. The result was 8 units, 2 business documents and 3 pending operations.

These checks are diagnostic evidence only. Repeat against the intended .NET 10 / EF Core 10 stack before use.

## Migration provenance and required follow-up

The initial migration and model snapshot were scaffolded with the available EF Core 8 diagnostic model. They apply successfully to diagnostic SQLite databases and are source-controlled as a starting point. Review the model against EF Core 10 and ASP.NET Core Identity 10, run `dotnet ef migrations has-pending-model-changes`, and create an additive migration for any differences **before** treating the .NET 10 database lifecycle as validated. Do not suppress pending-model warnings to bypass this step.

The initial migration is SQLite-specific and rejects other providers. SQL Server needs a separate migration assembly/set, clean-database integration tests and a reviewed deployment script.

## Remaining unverified areas

- Native MAUI Windows build and installation (the shared .NET 10 solution and EF Core 10 model are now validated).
- Browser interaction/visual rendering beyond authenticated server-rendered markup.
- SQL Server, Android/Windows MAUI, printer hardware, true disconnected client operation.
- Multi-device synchronization, upload replay/order/schema mismatch, central conflicts.
- Production security, report scale, load, backup restoration, installation/upgrade.


Installer 0.3.0 compiled successfully: 281872205 bytes; independently verified SHA-256: `F87750924123D72045395CBC97325E5C185661CF4A9A79D3F4D3B3A603093678`. Output: `artifacts/windows/0.3.0/SierraBusiness-Setup-x64.exe`. This is an unsigned development preview.



Installer 0.3.1 compiled successfully: 281879505 bytes. Independently recomputed SHA-256 matches SHA256SUMS.txt: 5536A1A49C7BC8A21906931E5CE4BAC6A58DC4D3B98C230A657336DC402AD464. Output: artifacts/windows/0.3.1/SierraBusiness-Setup-x64.exe. Unsigned development preview.


Final 0.4.0 package completed successfully after the UI fixes. All 91 tests and Windows OCR passed again; native publish and Inno Setup compilation succeeded. Installer size: 281924464 bytes. Independently verified SHA-256: D4E302592A6D2F8452676567755DF0DB995FBF0CA77A1C66CCD91C14FA4DB377. Final staging: artifacts/windows/staging-aadc821fa69f43d2b500e20a62976d3a. The published SharedUI assembly is newer than the final UI source edits. This remains an unsigned preview pending native/hardware/clean-machine acceptance.


Release 0.5.0 installer compilation completed successfully. The installer is 281,994,124 bytes; independently verified SHA-256: 2DD8D1C6374EE0126BEC59C0B73D26656FF94C5E408E6B505000B032AB8B34F6. The checksum matches artifacts/windows/0.5.0/SHA256SUMS.txt. Native staging: artifacts/windows/staging-1e37dd343a6a4885b9c6e551b53c35df. The app was packaged, not installed over the user's running copy.

## Release 0.6.0 verification — 2026-09-29

The completed packaging log records 108 passing tests (zero failures/skips), successful offline PDF/image OCR checks, native MAUI publishing and Inno Setup compilation. No application or test source files are newer than the installer. Installer product version is 0.6.0, size 282,020,580 bytes. An independently recomputed SHA-256 matches SHA256SUMS.txt: `A34215FBBB1DDF4C458A3B1B7E0415C9AAB25B6C99A34F7F3CDB16D94324F2F9`.

Output: `artifacts/windows/0.6.0/SierraBusiness-Setup-x64.exe`. This verification did not install over the user's copy. The package remains an unsigned development preview; physical scanners, native keyboard/printing and clean-machine installation/upgrade acceptance remain outstanding.

Release 0.7.0 installer compilation completed successfully. Size: 282011295 bytes. Independently recomputed SHA-256 matches SHA256SUMS.txt: F038E2BDDAD337A24291D0FB8D557EFF3B5B1C87723E958D806670C89C958015. Output: artifacts/windows/0.7.0/SierraBusiness-Setup-x64.exe. Product version verified as 0.7.0. Native staging: artifacts/windows/staging-90417168462947e3a9cb8bd0970f09f2. Unsigned development preview; not installed over the user's copy.

Release 0.8.0 installer compilation completed successfully. Size: 282019357 bytes. Independently recomputed SHA-256 matches SHA256SUMS.txt: 3334E9C19597BF0F5A0576AE390EC011D256FF470B0C6E4635EA2DC226550911. Output: artifacts/windows/0.8.0/SierraBusiness-Setup-x64.exe. Product version verified as 0.8.0. Native staging: artifacts/windows/staging-21e037b19d24412faade51f00db86142. Unsigned development preview; not installed over the user's copy.

Release 0.9.0 installer compilation completed successfully. All 125 tests, authenticated HTTP workflow checks, offline OCR, native MAUI publish and Inno Setup compilation passed. Size: 282017253 bytes. Independently recomputed SHA-256 matches SHA256SUMS.txt: 57439821D392B62B6B655899992FAA5BE8AA34F74F5CA8C15F85D89D56D8896F. Output: artifacts/windows/0.9.0/SierraBusiness-Setup-x64.exe. Product version verified as 0.9.0. Native staging: artifacts/windows/staging-83a7dfba6aea494a984b93ad429b4011. Unsigned development preview; not installed over the user's copy.

## Grouped navigation 0.10.0 — 2026-10-02

All 126 tests passed with zero failures/skips, including unique area mappings and permission-filtered groups. The full HTTP workflow passed and now asserts that the interactive Blazor script is served. Browser inspection found and fixed the direct-launch static asset 404. Live browser actions verified mobile menu expansion and navigation to Inventory and Finance/Accounting. DOM geometry at 1366×768 confirmed all area buttons/sign-out within the viewport and no horizontal document overflow; a 390×844 view was inspected for wrapping section links and table-local scrolling. Screenshot: docs/navigation-desktop.png. Build succeeded without compiler warnings/errors and Windows OCR checks passed at 94%.

The native host shares this Razor component and stylesheet. Full native/hardware and clean-machine acceptance remain outstanding. Installer results follow below.

Installer 0.10.0 completed successfully: 282032576 bytes. Independently verified SHA-256: 7C91703152301642F4B23B9AEF520A635E5675C1E81682DF04856BC04F2276C4. Version 0.10.0 verified. Output: artifacts/windows/0.10.0/SierraBusiness-Setup-x64.exe. Unsigned preview; not installed over the user's copy.
