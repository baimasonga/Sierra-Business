# Engineering skill pack conformance audit

Date: 2026-09-27. Scope: the 28 skills, global rules and Definition of Done in [the supplied specification](engineering-specification.md), compared with current source and the 0.1.0 Windows preview. This is not an audit of unrelated Codex plugin skills.

Follow-up: [release 0.1.1 fixes](bugfix-release-0.1.1.md) address the eight specific findings recorded below. The audit is retained as the 0.1.0 baseline. Missing modules and unverified production acceptance criteria are not resolved by those bug fixes.

**Verdict: the full specified system is NOT present or verified functional.** The installer delivers an initial local trading slice. No whole skill can be signed off against all of its implementation instructions and acceptance criteria. Passing tests demonstrate the tested slice, not completion of the specification. Do not use the existence of a Setup.exe as evidence of production readiness.

## Method and evidence

Inspected the complete source inventory, domain rules, application contracts, persistence and migration, endpoints, shared UI, desktop startup/accounts/backups, project targets and specification. Reran real SQLite tests on .NET 10 and added audit coverage for all three settlement accounts and their reversals, historical tax retention and read-permission filtering. Test results are in `tests/SierraBusiness.IntegrationTests/TestResults/feature-audit.trx`. No production database was used. Only tests and documentation were changed during this audit; no application features were silently added or fixed.

Evidence references used below:

**Execution result:** all **28** tests passed on .NET 10 (the original 23 plus five audit cases). EF Core reports no pending model changes. The shipped installer was built with the same application implementation before these test-only additions; it has not been rebuilt for this audit. These results do not mean 28 skills passed: there are 28 tests and 28 separate specification skills, coincidentally the same count.

- **Domain:** `src/SierraBusiness.Domain/Trading.cs` (three payment methods, three document kinds, fixed ledger accounts, tracking and accounting rules).
- **Service:** `src/SierraBusiness.Infrastructure/BusinessService.cs` (Read, AddProduct, Post, Reverse, atomic/idempotent execution).
- **Schema:** `src/SierraBusiness.Infrastructure/Store.cs` and `Migrations/` (five business entity types plus Identity; SQLite-only migration).
- **API/Web:** `src/SierraBusiness.Api/BusinessEndpoints.cs`, `src/SierraBusiness.Web/Program.cs`, `Components/Pages/Home.razor`.
- **UI:** `src/SierraBusiness.SharedUI/Workspace.razor` (eight owner pages; document detail is JSON).
- **Desktop:** `src/SierraBusiness.Desktop/` and `src/SierraBusiness.Maui/` (Windows target, accounts/session, backups, local database).
- **Tests:** `tests/SierraBusiness.IntegrationTests/TradingTests.cs` and `DesktopTests.cs`.
- **Package:** `scripts/package-windows.ps1`, `packaging/windows/SierraBusiness.iss`, [package verification](verification.md).

“Partial” means some capability exists but the complete skill is absent. “Missing” means no implementation of that module was found. “Unverified” means an implementation or intended behavior has not been exercised at the required boundary. Source inspection is not a UI acceptance test.

## All 28 skill areas

| # | Skill | Result | Present / evidence | Missing or unverified |
|---|---|---|---|---|
| 1 | Solution architecture | Partial | Domain, Application, Infrastructure, API, Web, SharedUI, Desktop and MAUI; shared business service | Sync/Workers projects, module boundaries/registration and architecture tests; domain model remains one trading slice |
| 2 | Domain/EF/SQL Server | Partial | GUID IDs, decimals, FKs, unique SKU/document keys, SQLite migration; relational tests | Central SQL Server migrations explicitly rejected; SQL Server tests, warehouse/company model, broader entities/concurrency |
| 3 | Offline/sync | Partial | Atomic local operation log, persistent SQLite, duplicate-command protection | Upload/pull, inbox, cursor, bootstrap, conflict handling, ordered replay, network recovery, multi-device tests, schema handshake |
| 4 | Shared UI design | Partial / UI unverified | Shared workspace, keyboard shortcuts Alt+N/Alt+R/Esc, labels, reversal confirmation | Command palette, reusable field/grid primitives, focus management, saved preferences, complete keyboard/tablet/accessibility acceptance |
| 5 | MAUI/POS client | Partial | Windows native app builds; local account/session/DB/backup services tested; startup observed | Android target/screens, scanner/printer adapters, draft recovery; disconnected native transaction flow and full upgrade UI acceptance |
| 6 | Web management | Partial | Identity sign-in, authorized page/API, shared RCL, antiforgery | Branch comparison, approvals/tender/service oversight, live business notifications, large report paging; browser visual acceptance |
| 7 | API/contracts | Partial | Versioned DTO commands for product, trade, reversal; application validation/idempotency | OpenAPI, paging, full module endpoints, sync compatibility, comprehensive HTTP error/auth tests |
| 8 | Identity/permissions | Partial | Owner bootstrap, lockout, permissions/branch checks; desktop expiry and revocation tests | User/role administration, full permission matrix, company/warehouse scope, manager PIN/approval; web session revocation gap below |
| 9 | Inventory/catalogue | Partial | Quantity, serial, batch/expiry, explicit conversions, weighted cost, oversell prevention | Locations/transfers/counts, reservations, FEFO, barcode master, price levels, reorder, structured variants/compatibility and complete serial states |
| 10 | Excel onboarding | Missing | None | Open XML dependency, templates, upload/mapping/staging, validation/preview, atomic imports, error export and retry tests |
| 11 | Retail POS | Partial | Invoice entry, cash/cheque/transfer/credit settlement, serial validation, full reversal | Scan workflow, till/shift/float/variance, discounts/overrides, hold/recall, exchange/partial return, offline printed receipt |
| 12 | Wholesale/orders | Partial | Direct invoice and receivable posting | Quotations/orders/reservations/delivery, quantity/customer pricing, PO fields, partial fulfillment and receipt allocation |
| 13 | Purchasing/imports | Partial | Goods receipt debits inventory and credits supplier payable | Supplier master, PO/invoice matching, FX, landed cost, partial order receipts, supplier settlement and valued purchase returns |
| 14 | Accounting | Partial | Balanced operational journals, fixed accounts, full reversals, configured date lock, trial balance | Configurable chart/profiles/period UI, journals/expenses, allocations, cheque lifecycle, P&L/BS/cash-flow and broader posting rules |
| 15 | Cash/cheques/bank | Partial | Sale settlement routes to Cash, ChequesInHand or Bank; reference/bank/date capture | Independent payments/allocations, deposit/clear/bounce states, reconciliation, cash shift totals and settlement controls |
| 16 | Quotations/tenders | Missing | None | Opportunity/bid/version/approval/attachment/order-conversion workflows |
| 17 | Equipment lifecycle | Partial | Serial receipt/sale/full reversal; duplicate serial checks | SerialUnit model, installation, warranty/service timeline, technical attributes, compatibility lookup |
| 18 | General merchandise | Partial | Canonical base quantities/conversions; separate SKUs can represent goods | Structured furniture attributes, bundles, reorder/preferred supplier, variant/unit barcodes |
| 19 | CRM/credit/collections | Partial | Named party text and receivable journal for credit invoices | Customer/contact master, terms/due dates/limits/holds, collection workflow, ageing, allocations and statements |
| 20 | Installation/warranty/service | Missing | None | Installation jobs, claims, service tickets, parts, schedules, engineer/field offline workflows |
| 21 | Reporting/SignalR | Partial | Stock/value, document register/detail, trial balance and basic overview | Financial/operational report suite, period filters/pagination, broad drill-down, branch KPIs; no application notification/reconnect workflow |
| 22 | Audit/approvals | Partial | Atomic actor/branch/action/payload/time operation records and reversal reason; audit read permission | ApprovalRequest/thresholds, device/approval metadata, anomaly controls/export; persistence audit protection gap below |
| 23 | Sierra Leone configuration | Partial | NLe display, decimal money, web tax option retained on posted sale | Effective-dated taxes, company/statutory metadata, configurable localization and fiscal adapter; desktop settings gap below; no compliance certification |
| 24 | Documents/printing/files | Partial numbering only | Offline-safe branch/device/operation-derived numbers, stored document details | Printable receipt/invoice/PDF, templates, printer/reprint workflow, attachment storage/authorization/sync; JSON detail is not a business invoice |
| 25 | Workers/resilience/diagnostics | Partial health shell | Basic web health endpoint, pending count | Background jobs, retries/checkpoints/dead-letter, dependency health checks, correlation logging, environment/version diagnostics |
| 26 | Migration/upgrades | Partial | Versioned SQLite migration, no model drift, replay with outbox preserved, backup/restore tests | Genuine old-to-new schema upgrade rehearsal, failed-upgrade recovery, central migrations, client/server compatibility matrix |
| 27 | Quality engineering | Partial | Real SQLite accounting/inventory/session/backup tests, CI definitions | SQL Server/API/UI/hardware/load suites, sync fault simulation, imports and full specification scenarios; no CI execution claimed |
| 28 | Deployment/operations | Partial | Windows installer generated/checksummed; native startup; local backup restore tested | Clean-machine install/upgrade/uninstall, release signing/update rollback, central hosting/configuration tests, automated SQL/attachment backups |

No percentage completion is reported: these skills have very different sizes, and counting partial modules would misrepresent readiness.

## Specific defects and control gaps found by source review

1. **Desktop trading configuration is not wired to persisted settings.** `DesktopRegistration.cs` only calls `AddOptions<TradingOptions>()`; `MauiProgram.cs` never binds a configuration source. The packaged desktop uses default FREETOWN/COUNTER01, zero tax and the default closed-through date. The web host does bind configuration. Owners cannot configure a desktop branch, tax or period lock through the app. This blocks practical multi-branch and fiscal deployment.
2. **Web permission revocation lacks the desktop's per-operation revalidation.** `Home.razor` captures Actor once; the API reads cookie claims. Web startup does not configure security-stamp revalidation, and BusinessService trusts the supplied actor. A permission removed from Identity is not reloaded by these paths. Desktop SessionBusinessService does reload it. Add HTTP/interactive-session revocation tests before accepting the web security model.
3. **Draft navigation can discard lines without confirmation.** `Workspace.Navigate` clears lines on entry to New sale or Receive stock, including repeated navigation to the same page. No persistent draft or unsaved-change confirmation exists. An accidental shortcut can lose an unfinished document.
4. **An entered unit price persists across product changes.** `AddLine` resets quantity/conversion/serial/batch but retains `linePrice`; adding another product may use the prior product's price. The same mutable fields are reused between receipt and sale entry. Add explicit price-source/reset behavior and UI tests.
5. **Overview sales do not reconcile to net sales after reversals.** The displayed sum selects only Sale documents; Reversal documents are excluded. A sale for 200 followed by a full reversal leaves 200 on the overview although net sales journal effect is zero. It is labeled POSTED SALES, so it may represent gross issued invoices, but it must not be represented as the required net/reconciled sales KPI. Tax is included in this sum, another distinction from revenue.
6. **Audit immutability is incomplete at the persistence boundary.** `BusinessDb.SaveChangesAsync` guards documents/movements/journal lines, but not Operation deletion or payload/actor changes, and synchronous SaveChanges has no equivalent guard. No current UI endpoint exposes such edits; this is a defense/control gap, not a demonstrated public API exploit. Acknowledgement updates need narrowly scoped treatment when sync is built.
7. **Variant and Compatibility options overstate implemented tracking.** The enum/UI exposes both, but no structured variant/compatibility entities or rules exist. Separate SKUs are a workaround for some variants, not implementation of these required models.
8. **Health endpoint does not measure database or storage health.** `AddHealthChecks()` registers no dependency checks. A healthy response is not evidence that trading persistence works.

These are audit findings, not fixes included in the existing installer. UI findings are based on the exact component code paths and have not been replayed through native UI automation in this audit.

## Global rules and release limits

The implemented methods use decimal money, explicit unit conversion, business-generated journals and movement-derived stock. Atomic commit, repeat delivery, rollback, serial/expiry constraints and branch denial have test evidence. Only Cash, Cheque and BankTransfer are payment enum values; credit is null settlement. No manufacturing/BOM, card, mobile money or cryptocurrency implementation was found.

The broader mandatory scope is not met: foreign currency, synchronization, all required business modules and multi-location operations are absent. Operation records lack a dedicated device/sequence field. UTC timestamps and entered business dates exist, but configurable branch-local date policy is not implemented. Merely restarting SQLite successfully does not test power loss. Cookie/permission controls and app-level history guards are not a production security certification.

Desktop/web screen rendering, keyboard-only sales, real printer/scanner behavior, Android, clean-machine offline installation, upgrade/uninstall, SQL Server and central network fault scenarios remain unverified or impossible to test until implemented. The .NET tests do not establish those results. No legal/tax compliance claim is made.

## Recommended completion order

1. Fix the configuration, session-revocation, draft/price-state and audit-protection gaps; add targeted regressions and UI/HTTP acceptance tests.
2. Complete organization/branch/location, customer/supplier, barcode/variant/unit masters and administration.
3. Complete inventory transfer/count/reservation/FEFO and Open XML onboarding.
4. Complete POS shifts/discounts/returns/printing, orders, settlements/cheques, purchasing/FX/landed cost and financial reports.
5. Implement SQL Server schema and versioned synchronization with fault/replay/conflict tests.
6. Implement institutional tenders, equipment installation/warranty/service and Android workflows.
7. Run the full acceptance checklist, clean-machine/upgrade tests, production security and operational release gates.

The [acceptance checklist](feature-acceptance-checklist.md) retains every acceptance bullet from the 28 skills so that nothing is silently omitted from future sign-off.
