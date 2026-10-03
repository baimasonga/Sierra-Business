# Sierra Business OS

A Microsoft .NET implementation based on [the supplied engineering specification](docs/engineering-specification.md). This repository contains an initial working vertical slice, not the full production system described in that specification.

**Service & maintenance 0.17.0:** service jobs from fault report to invoice — engineer assignment, diagnosis, labour, parts issued from stock once, explicit warranty/contract/goodwill decisions, customer acceptance, chargeable invoices (cash, cheque, transfer or on account) and supplier warranty claims; preventive maintenance plans that generate planned visits. See [service notes](docs/service-maintenance-0.17.0.md).

**Tenders & document previews 0.16.0:** tender register with bid costing, versions, price approval, locked submissions, won-to-sales-order conversion and lost/cancelled closure; document library card gallery with first-page pictures and in-place previews of PDF, Word, Excel, PowerPoint, TIFF scans and photos. See [tender notes](docs/tenders-document-previews-0.16.0.md).

**Document library 0.15.0:** capture and keep tenders, cheques, invoices, contracts, signed delivery notes and certificates (file, scanner or phone camera), with on-device text reading, fingerprints, versions, links to transactions/customers/cheques/equipment/installations, confidentiality, voiding and a deadlines list. Includes the plan for SQL Server Express and Azure SQL. See [document library notes](docs/document-library-0.15.0.md).

**Equipment & after-sales 0.14.0:** serial equipment lookup with full history, unit specifications, accessory/consumable compatibility, installation scheduling with commissioning checklists and certificates, and customer warranties that start at sale or installation (void on return, renewed on resale). See [equipment notes](docs/equipment-installation-warranty-0.14.0.md).

**Supplier invoices 0.13.0:** receipts can wait for the supplier's invoice (goods received not invoiced), invoice matching with price and exchange differences, foreign-currency purchase orders, and credit sales only to registered customers. See [supplier invoice notes](docs/supplier-invoices-0.13.0.md).

**Purchasing & imports 0.12.0:** effective-dated exchange rates, foreign-currency goods receipts and bills, foreign supplier payments with exchange gains/losses, and landed-cost allocation (value, quantity or manual) to stock on hand and cost of sales. See [purchasing notes](docs/purchasing-imports-0.12.0.md).

**Excel import 0.11.0:** product catalogue, opening stock (quantity, serial, batch/expiry), price update and stock-count imports with generated templates, column matching, row-level validation, error workbooks and retry-safe commits. See [Excel import notes](docs/excel-import-0.11.0.md).

**Records safety 0.10.2:** tamper-proof posted history (database guards plus a verifiable seal chain), automatic backups with an off-machine copy folder, a guarded restore screen, and separate approval of stock counts. See [records-safety notes](docs/records-safety-0.10.2.md).

**Navigation redesign 0.10.0:** compact business areas, contextual screen menus, fixed company and account controls, and a responsive mobile menu. See [navigation guide](docs/navigation-0.10.0.md).

**Serial counts 0.9.0:** physical counts now support individual serials, with present/absent checks, valuation, history uniqueness and reservation protection. See [serial-count instructions](docs/serial-counts-0.9.0.md).

**Earliest-expiry suggestions 0.8.0:** counter sales can add available batch lines in expiry order, excluding expired/reserved stock and existing draft allocations. Review batches and expiry dates before posting. See [workflow instructions](docs/earliest-expiry-0.8.0.md).

**Batch counts 0.7.0:** warehouse counts now support individual batches and expiry dates, with lot valuation, reservation protection and audited adjustments. See [batch-count instructions](docs/batch-counts-0.7.0.md).

**Counter-sales release 0.6.0:** offline sale hold/recall, barcode and SKU entry with pack sizes, product filtering, keyboard shortcuts and editable negotiated prices. See [counter-sales instructions](docs/counter-sales-0.6.0.md).

**Banking and cash-control release 0.5.0:** reviewed CSV statement matching, received-cheque deposit/clearance/bounce tracking, till opening/closing counts, and paid-purchase refunds or supplier credits. See [workflow instructions and limits](docs/banking-cash-control-0.5.0.md).

**Negotiated-sales release 0.4.0:** confidential minimum selling prices, negotiated prices, reservations, partial delivery notes with invoicing that issues stock only once, paid sales returns, partial refunds/customer credits and additional printable documents. **After upgrading, an authorized manager must configure minimum prices in Sales controls before quoting/selling existing products.** See [release instructions](docs/negotiated-sales-0.4.0.md).

**Accounting improvements 0.3.1:** cash-flow reporting, audited bank-match cancellation/replacement and printable financial statements. See [release notes](docs/accounting-improvements-0.3.1.md). All 80 integration tests, HTTP checks, offline OCR and native packaging pass; hands-on acceptance remains outstanding.

**Business workflows preview 0.3.0:** accounting, orders, warehouses, parties, partial returns, payroll, staff permissions, period controls, company selection and printable documents are now included. See [release notes](docs/business-workflows-0.3.0.md) and the [remaining implementation scope](docs/implementation-progress.md). Full non-manufacturing Tally parity is unfinished.

**Document capture release 0.2.0:** the Windows app now includes offline PDF/image OCR, single-page WIA scanning, a review inbox with original files and revision history, and explicit opt-in online text assistance. See [capture instructions, limits and verification](docs/document-capture.md). The browser host does not include this Windows capture screen.

**Bug-fix release:** [0.1.1 release notes](docs/bugfix-release-0.1.1.md) document the fixes, configuration-file instructions and remaining limits.

**Feature audit:** [all 28 skills](docs/feature-audit.md) and [139 acceptance criteria](docs/feature-acceptance-checklist.md) have been reviewed. The full specification is not implemented. The original audit is retained as a baseline; later releases fix its eight defects and add workflows. See the implementation progress and current validation for the latest status.

**Validation:** all 185 SQLite integration and screen tests pass on .NET 10, and the Windows OCR smoke runner verifies real PDF and image recognition. EF Core confirms no pending model changes. Native MAUI publish and Inno Setup compilation succeed. Physical scanner, live paid AI, clean-machine installation and full UI acceptance remain unverified. See [verification](docs/verification.md).

## Run locally

### Windows desktop package

The **Windows installer is ready** at `artifacts\windows\0.17.0\SierraBusiness-Setup-x64.exe`. Double-click it to install, open Sierra Business from the Start menu, and create your owner account on first use. Existing users should back up and close the app before updating. The package includes the application runtimes, offline WebView2 prerequisite and English OCR dependencies. It is an unsigned development preview; full clean-machine installation and UI acceptance testing are still outstanding. See [Windows package instructions and status](docs/windows-package.md).

Desktop owners can create integrity-checked backups from the workspace sidebar, including captured originals and review revisions. Backup restoration and permission checks pass in the primary .NET 10 suite (126 tests total). A Windows CI workflow is also prepared to build the native app and installer once this project is hosted in a GitHub repository; it has not yet been run.

### Browser development host

Prerequisite: .NET 10 SDK. A workspace-local SDK, when present, is automatically used by the PowerShell scripts.

```powershell
.\scripts\setup.ps1 -Email owner@example.com
.\scripts\start.ps1
```

Open http://localhost:5180 and sign in with the email and password you chose. There are no default credentials. Setup prompts for a password, applies versioned migrations and creates a branch-scoped owner. Do not run setup against production without reviewing migrations and taking a tested backup.

Start with **Catalogue** to create a product, **Receive stock** to record goods and supplier liability, then **New sale**. **Stock summary**, **Documents**, **Trial balance**, and **Audit & sync** reconcile the resulting records. The database starts empty; no example business activity is mixed with real records.

## Implemented scope

- Shared Razor workspace and ASP.NET Core Identity sign-in, lockout, cookie sessions, permission checks and fixed branch scope.
- Product creation; quantity, serial and batch/expiry validation; explicit unit conversions.
- Supplier goods receipts on account; sales by cash, cheque, bank transfer or customer credit.
- Atomic local commits containing business document, stock movements, balanced journals and audit/outbox operation.
- Weighted-average stock cost per product/tracking identity; no negative stock; serial uniqueness.
- Idempotent commands with identity/payload collision checks; append-only posted records; guarded full-document reversals.
- Stock summary, document register/detail, trial balance and audit/outbox diagnostics.
- Versioned authenticated API under `/api/v1`, using the same application service as the UI. Cookie-based writes require the antiforgery token from `/api/v1/antiforgery` in the `RequestVerificationToken` header.
- SQLite development migrations and SQL Server provider wiring. Central SQL Server migrations are not implemented; the initial SQLite migration explicitly refuses SQL Server execution.

## Important boundaries

The existing runnable diagnostic host is a local **Blazor Server** application. The **MAUI Windows host** embeds the shared UI and application services in-process with local SQLite and is packaged in the Windows installer; full native acceptance remains outstanding. The Android client and central synchronization transport remain unimplemented. Pending outbox records are visible and durable but are not uploaded anywhere.

Tax defaults to **zero as a development configuration, not a statement of Sierra Leone tax law**. `Trading:TaxRate` is stored on each posted sale. Effective-dated tax configuration and verified fiscal compliance remain required before production use.

Cheque tracking, reviewed bank reconciliation, bill allocations, credit limits, party masters and ageing are implemented; see the release instructions for their limits. Goods receipts recognize inventory/payables directly, or wait for supplier-invoice matching through goods received not invoiced (0.13.0). Foreign-currency purchasing and landed cost are implemented (0.12.0).

Read [implementation status](docs/implementation-status.md) before using the system with live records.

## Structure

`Domain` holds accounting and validation rules, with no framework dependencies. `Application` owns host-independent commands, actor permissions, DTOs and service contracts. `Infrastructure` implements persistence and transactions with EF Core. `Api` maps authenticated business commands. `SharedUI` depends only on application/domain contracts. `Web` is the composition root and identity host. There is no direct database access in Razor components.

## Configuration and operations

- `Database:Provider`: `Sqlite` (default). `SqlServer` wiring exists but must not be selected until its separate migration set and integration tests are implemented.
- `ConnectionStrings:Business`: external connection string, preferably supplied by environment variables in deployed environments.
- `Trading:Branch`, `Trading:Device`: fixed branch/device identifiers. Do not change them after posting transactions without a migration plan.
- `Trading:TaxRate`: fractional rate from 0 through 1. Obtain an appropriate rate from verified official requirements.
- `Trading:ClosedThrough`: transactions on or before this date are blocked.
- Currency is currently displayed as NLe with two decimal places. All business dates are entered explicitly; audit timestamps are UTC.

Use HTTPS and protected server configuration for any deployment beyond localhost. Owner/user administration UI, database permission separation, backup automation, restore rehearsal and deployment hardening are still required.

Stop the application before copying its SQLite database (`src/SierraBusiness.Web/App_Data/sierra.db`) for a consistent development backup. Keep the database and any journal/WAL files together; never edit posted rows manually. Test restoring a copy before relying on a backup.

## Verification

```powershell
dotnet restore SierraBusiness.sln
dotnet build SierraBusiness.sln --no-restore
dotnet test SierraBusiness.sln --no-build
```

Use `.tools\dotnet\dotnet.exe` instead of `dotnet` if the SDK is installed only in this workspace. The tests use real, temporary SQLite databases and apply migrations, not EF InMemory. CI repeats the build and tests on Windows.






