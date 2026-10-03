# Sierra Leone Business Operating System — Microsoft .NET Engineering Skill Pack

## Purpose
This skill pack defines how an AI coding agent should design, build, review, test, deploy, and extend the **Sierra Leone Business Operating System** using a Microsoft-first technology stack.

The product is an **offline-first Retail, Wholesale, Distribution, and Institutional Business Management Platform** for Sierra Leone businesses dealing in medical and laboratory equipment, computers and IT equipment, furniture, stationery, consumables, spare parts, and general merchandise.

The system must scale from a single retail counter to a multi-branch organization with warehouses, institutional customers, tenders, serialized equipment, service/warranty operations, and full double-entry accounting.

The interaction model is **inspired by TallyPrime's speed and business workflow philosophy**—gateway-style navigation, keyboard-first operation, voucher-like entry, dense reports, contextual actions, and drill-down—but must not copy Tally branding, proprietary assets, source code, wording, or pixel-for-pixel layouts.

---

# 1. MANDATORY MICROSOFT TECHNOLOGY BASELINE

Unless an existing repository already contains an explicitly approved equivalent, use the following stack.

| Layer | Required / Preferred Technology |
|---|---|
| Primary language | **C# 14** |
| Runtime | **.NET 10 LTS** |
| Desktop / Windows POS | **.NET MAUI Blazor Hybrid** |
| Android / tablet client | **.NET MAUI Blazor Hybrid** |
| Browser management portal | **ASP.NET Core 10 Blazor Web App** |
| Shared UI | **Razor Class Library (RCL)** |
| Backend | **ASP.NET Core 10 Web API / application endpoints** |
| Data access | **Entity Framework Core 10** |
| Offline database | **SQLite** |
| Central database | **SQL Server 2025**; Azure SQL is optional for hosted deployments |
| Authentication | **ASP.NET Core Identity** plus permission-based authorization |
| Real-time online updates | **ASP.NET Core SignalR** |
| Background jobs | **.NET Worker Services / BackgroundService** |
| Excel import/export | **Microsoft Open XML SDK**, with domain validation and staging |
| Logging | **Microsoft.Extensions.Logging** |
| Configuration | **Microsoft.Extensions.Configuration / Options pattern** |
| Resilience | **Microsoft.Extensions.Http.Resilience** or approved .NET resilience policies |
| API documentation | **OpenAPI** generated from ASP.NET Core endpoints |
| Cloud app hosting | **Azure App Service** optional |
| Cloud database | **Azure SQL Database** optional |
| Attachments | **Azure Blob Storage** optional, with local/file-system abstraction for on-premise |
| CI/CD | GitHub Actions or Azure DevOps using `dotnet` CLI |

### Technology rules
1. Do not introduce Node.js, Python, Java, PHP, Electron, Tauri, Supabase, PostgreSQL, Firebase, or another primary application stack unless the user explicitly changes the architecture.
2. JavaScript/TypeScript may be used sparingly only when a browser component cannot reasonably be implemented with Blazor/C#.
3. Business rules must never be duplicated independently in MAUI and Web clients. Put them in shared .NET domain/application libraries.
4. UI projects must not perform direct SQL access.
5. EF Core is the default data access mechanism. Raw SQL is allowed only for measured performance or specialized reporting needs and must be encapsulated and tested.
6. The offline client must remain usable without Azure, internet access, or a central server connection.
7. Azure services are deployment options, not hard dependencies.

---

# 2. GLOBAL PRODUCT RULES

These requirements apply to every skill and implementation task.

1. **Manufacturing and BOM are out of scope.** Do not add recipes, production orders, work orders for manufacturing, or bills of materials unless explicitly requested later.
2. **Accepted payment methods are only:**
   - Cash
   - Cheque
   - Bank Transfer
   Credit is a sales condition, not a payment method.
3. Core transactions must be **offline-first**.
4. Primary currency is **SLE / NLe**. Foreign-currency purchasing and configurable exchange rates are required.
5. The platform must support:
   - Retail
   - Wholesale
   - Distribution
   - Institutional sales
   - Quotations
   - Tenders
   - Purchases and imports
   - Landed cost
   - Multi-branch and multi-warehouse inventory
   - Serial-number tracking
   - Batch/lot and expiry tracking
   - Installation and commissioning
   - Warranty
   - Service and repairs
   - Accounting and financial reporting
6. Product tracking modes must include:
   - Quantity-only
   - Serial-number controlled
   - Batch/lot controlled
   - Batch + expiry controlled
   - Variant controlled
   - Compatibility controlled
7. Product sale units and purchase units may differ. Unit conversions must be explicit and auditable.
8. Accounting is automatic underneath operational transactions. Ordinary cashiers must not need to understand debit/credit bookkeeping.
9. Every material transaction must be auditable. Do not allow privileged users to silently erase history.
10. Regulatory values and Sierra Leone-specific rules must be configuration-driven, not hard-coded.
11. Inventory balances must be derived from stock movements; never allow clients to overwrite stock totals directly.
12. Financial postings must be derived from business documents through the accounting posting engine.
13. All externally supplied writes, including Excel imports and sync payloads, must be idempotent.
14. Store dates/times with a clear UTC/local-time policy and preserve branch/local business date semantics.
15. Design for unreliable electricity and connectivity: short transactions, durable local commits, resumable synchronization, and safe restart after crashes.

---

# 3. TARGET SOLUTION STRUCTURE

Use a modular monolith with shared domain/application layers. A recommended repository layout is:

```text
SierraBusiness.sln

src/
  SierraBusiness.Domain/
  SierraBusiness.Application/
  SierraBusiness.Infrastructure/
  SierraBusiness.Api/
  SierraBusiness.Web/
  SierraBusiness.Maui/
  SierraBusiness.SharedUI/
  SierraBusiness.Sync/
  SierraBusiness.Workers/

modules/
  SierraBusiness.Modules.Identity/
  SierraBusiness.Modules.Companies/
  SierraBusiness.Modules.Catalogue/
  SierraBusiness.Modules.Inventory/
  SierraBusiness.Modules.Sales/
  SierraBusiness.Modules.Retail/
  SierraBusiness.Modules.Purchasing/
  SierraBusiness.Modules.Customers/
  SierraBusiness.Modules.Suppliers/
  SierraBusiness.Modules.Payments/
  SierraBusiness.Modules.Banking/
  SierraBusiness.Modules.Accounting/
  SierraBusiness.Modules.Quotations/
  SierraBusiness.Modules.Tenders/
  SierraBusiness.Modules.Equipment/
  SierraBusiness.Modules.Service/
  SierraBusiness.Modules.Reporting/
  SierraBusiness.Modules.Audit/
  SierraBusiness.Modules.Importing/

contracts/
  SierraBusiness.Contracts/

tests/
  SierraBusiness.UnitTests/
  SierraBusiness.IntegrationTests/
  SierraBusiness.ApiTests/
  SierraBusiness.SyncTests/
  SierraBusiness.AccountingTests/
  SierraBusiness.InventoryTests/
  SierraBusiness.UiTests/
```

### Dependency direction

```text
Domain
  ↑
Application
  ↑
Infrastructure
  ↑
API / Web / MAUI / Workers
```

UI code must depend on application contracts, not on EF Core DbContexts.

---

# 4. CROSS-CUTTING ARCHITECTURE PRINCIPLES

### Modular monolith first
Keep deployable complexity low. Modules may have independent schemas/namespaces and domain boundaries, but initially run in one backend process unless a real scale or isolation requirement justifies extraction.

### Clean domain boundaries
Use aggregates and application services for business invariants. Do not create a generic CRUD system that lets screens update arbitrary entity properties.

### Transactional integrity
For any operation affecting stock and accounting, either the complete operation commits or none of it does.

### Outbox/inbox
Use the transactional outbox for events leaving a local or central database. Use an inbox/idempotency store for received events.

### Optimistic concurrency
Use row-version/concurrency tokens for mutable central records where appropriate. Inventory and ledger data should usually be append-oriented rather than last-write-wins.

### Configuration over hard-coding
Use strongly typed `IOptions<T>` configuration for company defaults, fiscal/tax rules, document numbering, approval limits, and environment-specific settings.

### Observability
Every critical workflow must produce structured logs and correlation IDs without leaking secrets or unnecessary personal data.

---

# SKILL 1 — MICROSOFT SOLUTION ARCHITECTURE & MODULE BOUNDARIES

## Trigger
Use when starting the repository, reorganizing projects, adding a major module, or changing dependencies.

## Objective
Maintain a modular .NET architecture that supports desktop, web, Android, offline operation, and a shared business core without duplicating rules.

## Microsoft technologies
- .NET 10 solution/projects
- C# 14
- ASP.NET Core 10
- .NET MAUI Blazor Hybrid
- Blazor Web App
- Razor Class Library

## Implementation instructions
- Create explicit projects for Domain, Application, Infrastructure, API, Web, MAUI, SharedUI, Sync, and Workers.
- Use project references so Domain has no dependency on EF Core, UI frameworks, HTTP, or storage.
- Place module contracts/events in stable namespaces. Avoid a giant Shared project containing business logic.
- Use dependency injection through `Microsoft.Extensions.DependencyInjection` and module registration extensions such as `AddInventoryModule()`.
- Create architecture tests that reject forbidden dependency directions when practical.

## Non-negotiable rules
- No UI component may instantiate DbContext directly.
- No module may write another module's database tables without an application/domain contract.
- New capabilities must name their owning module and dependencies before implementation.

## Acceptance criteria
- Solution builds from `dotnet build`.
- Module dependencies are documented.
- No circular project references.
- Desktop and Web clients consume the same application contracts.

---

# SKILL 2 — DOMAIN MODEL, EF CORE 10 & SQL SERVER DESIGN

## Trigger
Use for entities, value objects, aggregates, DbContexts, SQL schemas, constraints, migrations, or performance-sensitive queries.

## Objective
Create a normalized, auditable model that supports trading, stock control, serialized equipment, batch/expiry, service, and accounting.

## Microsoft technologies
- Entity Framework Core 10
- SQL Server 2025
- SQLite
- EF Core migrations
- SQL Server rowversion where appropriate

## Implementation instructions
- Model IDs as stable GUID/ULID-style application identifiers or another approved collision-safe strategy suitable for offline creation.
- Use owned/value types for Money, Quantity, Address, DocumentNumber, and similar concepts when beneficial.
- Create unique constraints for document numbers within their scope, serial numbers where required, barcodes, and idempotency keys.
- Use decimal types with explicit precision for money and quantities. Never use float/double for currency.
- Keep central and local schemas compatible with the sync contract; local-only sync metadata may live in separate tables.
- Use append-only movement/journal tables for inventory and accounting truth; balances are projections/caches.
- Generate and review every EF Core migration. Never rely on `EnsureCreated` for production databases.

## Non-negotiable rules
- No cascade delete may destroy posted transactions.
- Posted documents use void/reversal/amendment patterns instead of destructive editing.
- SQL Server and SQLite differences must be covered by integration tests when a query or constraint behaves differently.

## Acceptance criteria
- Migrations apply to a clean SQL Server database.
- SQLite local database initializes from a versioned migration path.
- Uniqueness, foreign keys, money precision, and concurrency rules are tested.

---

# SKILL 3 — OFFLINE-FIRST SQLITE & SYNCHRONIZATION ENGINE

## Trigger
Use for offline mode, branch sync, retry logic, data conflicts, client caches, background upload/download, or resilience.

## Objective
Allow every core branch/POS workflow to continue safely without internet and synchronize when connectivity returns.

## Microsoft technologies
- SQLite
- EF Core SQLite provider
- ASP.NET Core sync endpoints
- .NET BackgroundService
- HttpClientFactory
- resilience handlers

## Implementation instructions
- Commit the business transaction and an outbox record in the same local SQLite transaction.
- Assign every locally created operation a globally unique operation/event ID, device ID, branch ID, business date, and monotonic local sequence where useful.
- Upload outbox events in ordered batches; server processing must be idempotent through an inbox/idempotency table.
- Use server-issued cursors/change versions for pull synchronization instead of relying only on timestamps.
- Do not sync calculated stock totals as authoritative values. Sync stock movements and rebuild/projection-adjust balances.
- Do not use last-write-wins for posted invoices, stock movements, ledger entries, serial assignments, or cheque status changes.
- Expose sync health in the client: last successful sync, pending count, failed count, retry state, and branch/device identity.
- Provide resumable bootstrap for a new device with scoped product/customer/stock/reference data.

## Non-negotiable rules
- The POS must remain operational during network loss.
- Retries must never duplicate a sale, payment, stock movement, or journal posting.
- Conflicts requiring a human decision must enter an explicit conflict queue; never silently discard data.

## Acceptance criteria
- Offline sale then reconnect sync.
- Duplicate event delivery.
- Power interruption after local commit but before upload.
- Two offline devices selling stock.
- Server unavailable for extended periods.
- Schema-version mismatch handling.

---

# SKILL 4 — TALLY-INSPIRED SHARED UI DESIGN SYSTEM

## Trigger
Use for navigation, keyboard behavior, layouts, tables, forms, transaction screens, report screens, and shared components.

## Objective
Create a fast business UI familiar to Tally users while remaining original, accessible, mouse/touch-friendly, and reusable across MAUI and Web.

## Microsoft technologies
- Blazor/Razor components
- Razor Class Library
- CSS isolation
- MAUI BlazorWebView
- Blazor Web App

## Implementation instructions
- Build shared primitives: AppShell, GatewayPanel, ContextActionBar, ShortcutBar, DataGrid, DrillDownRow, MoneyField, QuantityField, SearchPicker, DocumentHeader, StatusBadge, ConfirmationDialog.
- Implement a universal Go To command/search surface analogous in spirit to Tally navigation, but use original labels/design.
- Support keyboard-first flows: Enter/Tab progression, Esc back/cancel, configurable function-key shortcuts, Ctrl+S/Ctrl+A equivalent save/accept where appropriate.
- Keep transactional screens dense and fast. Avoid excessive cards, animation, whitespace, and modal chains.
- Provide focus management after barcode scans and after line acceptance.
- Use responsive adaptations: desktop dense view, tablet touch targets, web management layout.
- Persist user preferences such as visible columns and report filters per user/device when safe.

## Non-negotiable rules
- Do not copy Tally logos, proprietary icons, exact screen text, or pixel measurements.
- Keyboard shortcuts must have clickable equivalents.
- All destructive/high-risk actions require explicit confirmation and authorization.

## Acceptance criteria
- Gateway renders in MAUI and Web.
- Keyboard navigation completes a sale without mouse.
- Screen-reader labels and focus order are reasonable.
- 1024px tablet layout remains usable.

---

# SKILL 5 — .NET MAUI BLAZOR HYBRID DESKTOP / POS CLIENT

## Trigger
Use for Windows POS, local hardware access, desktop packaging, local database lifecycle, or Android client behavior.

## Objective
Build the primary offline-capable operational client for Windows and Android while sharing Razor UI and C# application logic.

## Microsoft technologies
- .NET MAUI
- Blazor Hybrid
- SQLite
- MAUI dependency injection
- platform services

## Implementation instructions
- Keep platform-specific hardware integrations behind interfaces such as `IReceiptPrinter`, `IBarcodeScanner`, `ILocalFileStore`, and `IDeviceIdentity`.
- Use SQLite locally and run migrations/upgrade checks before opening transactional screens.
- Provide an offline authentication/session strategy appropriate to the security model, with cached permissions and controlled expiry.
- Implement crash-safe draft recovery for long documents such as quotations/tenders where helpful.
- Package Windows builds with predictable install/update procedures and signed releases where production infrastructure supports it.
- On Android, prioritize inventory lookup, stock count, service, receiving, and management workflows; do not assume a phone is the primary full POS unless required.

## Non-negotiable rules
- Do not place domain rules in platform-specific code.
- Device services must degrade gracefully when hardware is missing.
- Never block sale entry solely because the central server is unreachable.

## Acceptance criteria
- Windows offline startup.
- Local DB upgrade.
- Barcode scanner keyboard-wedge flow.
- Printer unavailable handling.
- Android shared screen rendering.

---

# SKILL 6 — BLAZOR WEB MANAGEMENT PORTAL

## Trigger
Use for browser-based administration, management reporting, master-data maintenance, approvals, or head-office workflows.

## Objective
Provide a secure web portal sharing components and contracts with the MAUI client while remaining central-server oriented.

## Microsoft technologies
- ASP.NET Core 10 Blazor Web App
- Razor Class Library
- ASP.NET Core Identity
- SignalR

## Implementation instructions
- Use server-side application services/API boundaries rather than exposing DbContext to components.
- Use shared RCL components where interaction requirements match MAUI; create web-specific shells only where necessary.
- Optimize management pages for branch comparison, approvals, audit review, tenders, service oversight, and financial reporting.
- Use SignalR for live online updates such as approval requests, stock changes, and sync status; the system must still work without SignalR.
- Use anti-forgery, secure cookies/tokens as appropriate, authorization policies, and server validation on all writes.

## Non-negotiable rules
- Web UI is not an excuse to bypass offline/domain workflows.
- Authorization must be enforced server-side even when controls are hidden in UI.

## Acceptance criteria
- Unauthorized URL access denied.
- Shared components render consistently.
- Live update reconnect works.
- Large report pagination/filtering performs acceptably.

---

# SKILL 7 — ASP.NET CORE API, APPLICATION SERVICES & CONTRACTS

## Trigger
Use for backend endpoints, integration contracts, sync endpoints, external integrations, or mobile/web communication.

## Objective
Expose stable, secure, idempotent business operations rather than CRUD access to database tables.

## Microsoft technologies
- ASP.NET Core 10
- OpenAPI
- System.Text.Json
- HttpClientFactory
- ProblemDetails

## Implementation instructions
- Organize endpoints by business capability. Commands should represent actions such as PostSale, ReceivePayment, DepositCheque, TransferStock, ImportInventory—not generic UpdateEntity.
- Validate request structure and then execute domain/application validation before persistence.
- Return consistent ProblemDetails/error codes suitable for offline retry and user-friendly messaging.
- Use explicit API contract DTOs and version them when breaking changes are unavoidable.
- Require idempotency keys for sync/import/externally retried writes.
- Use pagination/cursors for large reads and server-side filtering for reports/catalogue searches.
- Generate OpenAPI and include representative examples for integration-critical endpoints.

## Non-negotiable rules
- Do not expose EF entities as public API contracts.
- Do not accept client-calculated journal entries or stock balances from ordinary transaction endpoints.

## Acceptance criteria
- Idempotency test.
- Validation and authorization tests.
- Backward compatibility test for sync contract.
- ProblemDetails consistency.

---

# SKILL 8 — IDENTITY, ROLES & PERMISSION-BASED AUTHORIZATION

## Trigger
Use for login, user management, roles, branch access, approvals, or privileged operations.

## Objective
Use ASP.NET Core Identity as the identity foundation while enforcing granular business permissions and branch/company scopes.

## Microsoft technologies
- ASP.NET Core Identity
- Authorization policies/handlers
- Data Protection
- MAUI secure storage where appropriate

## Implementation instructions
- Define roles such as Owner, Administrator, Accountant, Branch Manager, Cashier, Salesperson, Procurement, Warehouse Officer, Service Engineer, Auditor.
- Define granular permissions such as `sales.create`, `sales.discount.override`, `sales.void`, `inventory.adjust`, `cheque.clear`, `accounting.post_journal`, `period.close`, `tender.approve`.
- Apply company/branch/warehouse scoping in authorization handlers or application services.
- Cache only the minimum permission context required for offline operation and record offline privileged actions for later review.
- Require step-up approval/PIN or manager authorization for configurable high-risk POS operations.

## Non-negotiable rules
- Role membership alone is not sufficient for sensitive actions.
- Never trust client-side hidden buttons as authorization.
- Disable/delete users without deleting their historical audit identity.

## Acceptance criteria
- Permission matrix tests.
- Cross-branch access denial.
- Offline cached permission expiry.
- Disabled-user sync behavior.

---

# SKILL 9 — PRODUCT CATALOGUE, INVENTORY, SERIALS, BATCHES & WAREHOUSES

## Trigger
Use for products, categories, stock, warehouses, serial numbers, batches, expiry, variants, compatibility, or stock counts.

## Objective
Provide one flexible inventory engine for stationery, furniture, computers, medical equipment, consumables, and general merchandise.

## Microsoft technologies
- EF Core 10
- SQL Server/SQLite
- domain events
- inventory projections

## Implementation instructions
- Model Product with tracking mode, base unit, optional purchase/sales units, barcode(s), category, brand/model, tax profile, active state, price levels, and reorder settings.
- Model StockLocation for branch store, warehouse, showroom, or approved vehicle/agent stock point.
- Every quantity change creates an InventoryMovement with source document, branch/location, quantity, unit conversion, cost basis, user, time, and operation ID.
- Serial-controlled items require one unique serial per physical unit and explicit status transitions: InStock, Reserved, Sold, Returned, InService, Transferred, Retired as applicable.
- Batch-controlled items store lot/batch, received/manufactured metadata when available, expiry, quantity, and location. Use FEFO recommendations for expiring goods.
- Support reservations for approved sales orders/tenders without pretending reserved stock is already sold.
- Stock count creates a count session and adjustment document after approval; never simply edit the balance.

## Non-negotiable rules
- Negative stock behavior must be configurable but controlled; serialized stock cannot go negative.
- A serial cannot be sold twice or exist in two locations simultaneously.
- Unit conversions must not accumulate floating-point error.

## Acceptance criteria
- Serial lifecycle.
- Batch FEFO.
- Multi-unit purchase/sale.
- Transfer between locations.
- Concurrent sale protection.
- Stock count adjustment.

---

# SKILL 10 — EXCEL INVENTORY IMPORT & BULK STOCK ONBOARDING

## Trigger
Use whenever products, opening stock, prices, serial numbers, batches, expiry, barcodes, or warehouse allocations need bulk Excel import.

## Objective
Provide safe, auditable Excel onboarding that passes every row through the same domain rules as manual entry.

## Microsoft technologies
- Microsoft Open XML SDK
- EF Core staging tables
- ASP.NET Core upload endpoints
- Blazor import wizard

## Implementation instructions
- Provide downloadable `.xlsx` templates generated by the system. At minimum support Product Catalogue, Opening Stock, Serial Inventory, Batch/Expiry Inventory, Price Update, and Stock Count templates.
- Accept user spreadsheets through an import wizard: Upload → Select Sheet → Map Columns → Validate → Preview → Resolve Errors → Commit.
- Parse `.xlsx` with Open XML SDK. Do not automate desktop Excel and do not require Microsoft Office to be installed.
- Stage normalized rows into ImportBatch/ImportRow records. Store row number, raw values needed for diagnostics, mapped values, status, warning/error codes, and final entity IDs.
- Validate duplicate SKU/barcode/serial, unknown warehouse, invalid unit, invalid decimal/date, missing required fields, negative/invalid quantity, serial count mismatch, expired batch warnings, and duplicate rows.
- For opening stock, commit through an OpeningStock document/InventoryMovement workflow—not direct StockBalance inserts.
- For serialized products, quantity must equal the count of accepted unique serials unless an explicitly supported serial range/template expands them deterministically.
- For batches, require batch number and optionally expiry according to product tracking rules.
- Allow dry-run/preview and downloadable error workbook or CSV so users can correct source data.
- Commit large imports in bounded batches with a clear all-or-nothing or partial-commit policy selected by import type; default opening stock to controlled atomic batches per document/location.
- Use a deterministic import idempotency key so retrying the same committed import does not duplicate products or stock.
- Keep import history and support corrective reversal through domain documents where financially/stock materially posted; do not delete evidence.

## Non-negotiable rules
- Excel must never write directly to inventory, ledger, or balance tables.
- Formula cells must be handled predictably; reject unsupported dynamic formulas or rely on stored cached values with clear validation.
- Limit file size/row count and scan file type safely; never execute macros.

## Acceptance criteria
- 1,000+ product import.
- Duplicate serial rejection.
- Opening stock by multiple warehouses.
- Batch/expiry import.
- Malformed workbook.
- Retry committed import.
- Error workbook generation.

---

# SKILL 11 — RETAIL POS & CASHIER OPERATIONS

## Trigger
Use for retail checkout, barcode scanning, till shifts, returns, receipts, cashier closing, or counter sales.

## Objective
Build a very fast offline POS suitable for stationery, computers/accessories, furniture, medical consumables, and equipment retail.

## Microsoft technologies
- MAUI Blazor Hybrid
- SQLite
- shared Razor components
- printer/scanner abstractions

## Implementation instructions
- Optimize the sale path: customer (default Walk-in) → scan/search → quantity → price/discount → payment → accept → receipt.
- Payment buttons must be Cash, Cheque, Bank Transfer only. Credit sale is a separate settlement state/terms choice, not a payment button.
- Support retail/wholesale/customer price levels and quantity breaks while recording the price source and overrides.
- Capture required serial number selection before posting serial-controlled lines.
- For cheque/bank transfer at retail, collect the required payment detail record before final acceptance.
- Support hold/recall draft sale, return against original invoice, exchange, supervisor override, and reason codes.
- Implement cashier shift: opening float, cash receipts/payments, expected cash, counted cash, variance, supervisor signoff.
- Generate receipt/invoice locally even while offline and queue central sync.

## Non-negotiable rules
- Posted sales are not deleted. Use void/reversal per authorization rules.
- Cashier cannot manually alter stock balances or accounting entries.
- Discount thresholds are configurable and approval-controlled.

## Acceptance criteria
- Keyboard-only sale.
- Offline sale.
- Cash sale.
- Cheque sale.
- Bank-transfer sale.
- Serial sale.
- Return/exchange.
- Cash drawer variance.

---

# SKILL 12 — SALES, WHOLESALE, DISTRIBUTION & INSTITUTIONAL ORDERS

## Trigger
Use for non-POS sales, customer pricing, sales orders, delivery notes, reservations, institutional customers, or distributors.

## Objective
Use one sales engine for retail, wholesale, distribution, and institutional workflows with different UX and controls.

## Microsoft technologies
- Application services
- EF Core
- Blazor/MAUI shared forms
- accounting/inventory domain events

## Implementation instructions
- Support Quotation → Sales Order → Reservation → Delivery Note → Invoice → Receipt allocation where the business uses the full flow.
- Allow simplified Invoice/Sale directly for businesses that do not need orders.
- Price resolution order must be deterministic: contract/customer price → price level → quantity tier → allowed promotion/override.
- Capture delivery address, contact, PO/reference number, sales representative, branch, terms, and optional project/tender reference.
- Partial delivery and partial invoicing must preserve line quantities and remaining obligations.
- Credit sales create receivables; later cash/cheque/bank receipts allocate to one or more invoices.

## Non-negotiable rules
- Do not treat customer credit as a payment method.
- Delivery must not consume stock twice when invoice follows a delivery note; define one authoritative stock-issue event in the workflow.

## Acceptance criteria
- Wholesale quantity pricing.
- Partial delivery.
- Sales order reservation.
- Institutional PO reference.
- Credit invoice then later payment allocation.

---

# SKILL 13 — PURCHASING, IMPORTS & LANDED COST

## Trigger
Use for suppliers, purchase orders, goods receipt, foreign currency, import shipments, or landed-cost allocation.

## Objective
Capture the full cost of imported and local goods while keeping stock quantities, supplier liabilities, and accounting synchronized.

## Microsoft technologies
- EF Core
- decimal Money types
- FX configuration
- application posting services

## Implementation instructions
- Support Purchase Order → Goods Receipt → Supplier Invoice → Supplier Payment, while allowing simplified local purchase entry.
- Allow purchase currency different from functional currency; store source currency, rate, source amount, and local amount.
- Model ImportShipment/LandedCostDocument with freight, insurance, clearing, customs/duties, port charges, bank charges, and local transport.
- Allocate landed costs by configurable basis: value, quantity, weight/volume when modeled, or manual allocation; persist the chosen basis and calculation.
- Update inventory valuation through approved cost adjustments, never by editing historical sale prices or balances.
- Handle partial receipts and outstanding purchase quantities.

## Non-negotiable rules
- Exchange rates are configuration/reference data with effective dates.
- Landed-cost recalculation after downstream sales requires explicit accounting/valuation behavior and tests.

## Acceptance criteria
- Foreign-currency purchase.
- Partial goods receipt.
- Landed-cost allocation.
- Supplier payment.
- Purchase return.

---

# SKILL 14 — DOUBLE-ENTRY ACCOUNTING ENGINE

## Trigger
Use for ledgers, journals, posting rules, financial statements, period close, reversals, or any feature that affects financial balances.

## Objective
Provide a strict double-entry engine where operational documents automatically create balanced accounting postings.

## Microsoft technologies
- C# domain services
- EF Core transactions
- SQL Server/SQLite decimal precision
- accounting projection queries

## Implementation instructions
- Define ChartOfAccounts, LedgerAccount, JournalEntry, JournalLine, PostingRule/PostingProfile, FiscalPeriod, Currency, ExchangeRate, TaxCode.
- Every posted JournalEntry must have total debits equal total credits in functional currency.
- Operational modules submit posting intents/events; only the AccountingPostingEngine constructs journal entries.
- Implement standard flows for sale, cash receipt, cheque receipt/clearance/bounce, bank transfer receipt, purchase, supplier payment, sales return, purchase return, stock adjustment, landed-cost capitalization, expense, and journal voucher.
- Create immutable posted journal entries. Corrections use reversals and replacement entries with references.
- Implement period locking/closing so ordinary users cannot back-date into closed periods.
- Build trial balance first; derive P&L and Balance Sheet from chart classifications and validated journal data.
- Use configurable tax posting rules and avoid embedding a tax rate in code.

## Non-negotiable rules
- UI may never submit arbitrary debit/credit lines for normal sales/purchase workflows.
- Inventory and accounting documents must commit consistently where the architecture expects a single local transaction.
- Never use floating point for money.

## Acceptance criteria
- Balanced-entry invariant.
- Sale posting.
- Credit sale/payment allocation.
- Cheque lifecycle postings.
- Return reversal.
- Closed period rejection.
- Trial balance totals.

---

# SKILL 15 — CASH, CHEQUE & BANK TRANSFER MANAGEMENT

## Trigger
Use for payments, receipts, bank accounts, cheque processing, bank transfers, bank reconciliation, or cashier settlement.

## Objective
Implement the agreed payment scope precisely and create traceable settlement workflows.

## Microsoft technologies
- Accounting module
- EF Core
- Blazor/MAUI payment dialogs
- bank reconciliation services

## Implementation instructions
- Model Payment/Receipt separately from allocation so one receipt may settle multiple invoices and one invoice may have multiple receipts.
- Cash captures cash account/till, branch, cashier, amount, date, reference, and allocation.
- Cheque captures cheque number, drawer/customer, bank, issue date, received date, amount, deposit account/date, clearance date, bounce reason, and status.
- Cheque state machine: Received → Deposited → Cleared, or Deposited → Bounced; additional cancellation states require authorization and accounting reversal.
- Bank Transfer captures destination/source bank account, transfer reference, transfer date, amount, customer/supplier, verification/reconciliation state, and attachment reference if configured.
- Bank reconciliation imports or enters statement lines, proposes matches, and records user-confirmed reconciliation without rewriting original payments.

## Non-negotiable rules
- Do not add card/mobile-money wallets unless scope changes.
- A bounced cheque must reopen/reinstate the relevant receivable/accounting effect according to posting rules.
- Cheque numbers may repeat across banks; uniqueness scope must be realistic.

## Acceptance criteria
- Partial invoice payment.
- One receipt allocated to several invoices.
- Cheque clear.
- Cheque bounce.
- Bank transfer reconciliation.
- Cash shift totals.

---

# SKILL 16 — QUOTATIONS, TENDERS & INSTITUTIONAL PROCUREMENT SALES

## Trigger
Use for quotations, government/NGO/corporate bids, tender documents, bid costing, deadlines, or conversion to orders.

## Objective
Support businesses that sell to hospitals, ministries, NGOs, schools, companies, and other institutions through formal quotation/tender processes.

## Microsoft technologies
- Blazor forms
- EF Core
- document storage abstraction
- background reminders optional

## Implementation instructions
- Model TenderOpportunity with reference, institution, title, opening/closing dates, status, responsible officer, items, documents, notes, and submission metadata.
- Support supplier cost estimates, landed cost assumptions, margin, local currency bid price, taxes/configuration, and alternative product offers.
- Status flow should include configurable states such as Draft, BidPreparation, Submitted, Clarification, Won, Lost, Cancelled.
- Convert an approved/won quotation/tender into sales order/reservation without re-keying accepted lines.
- Keep bid versions and approval trail for material price changes.
- Allow attachments such as tender documents, technical specifications, compliance certificates, and submission receipts.

## Non-negotiable rules
- Do not overwrite a submitted tender version silently.
- Tender status changes and pricing approvals are auditable.

## Acceptance criteria
- Tender creation.
- Bid revision/version.
- Approval.
- Won tender conversion.
- Lost tender closure.
- Attachment authorization.

---

# SKILL 17 — MEDICAL & IT EQUIPMENT SERIAL LIFECYCLE

## Trigger
Use for serialized medical equipment, computers, laboratory devices, warranty identity, installation, or asset traceability.

## Objective
Track each high-value serialized unit from purchase through sale, installation, warranty, service, return, and retirement.

## Microsoft technologies
- Inventory serial entities
- Service module
- EF Core
- shared equipment views

## Implementation instructions
- Product defines model/specifications; SerialUnit represents the individual physical unit.
- Capture supplier, purchase receipt, cost, serial, current location/status, customer sale, invoice, delivery, installation, warranty, and service history.
- For IT equipment, support structured specifications such as processor, RAM, storage, screen, and configurable custom attributes.
- For medical/lab equipment, support installation/commissioning checklist, training record, accessories, and service schedule where required.
- Provide universal serial search showing current state and complete document timeline subject to permissions.
- Model compatible accessories/consumables as relationships in catalogue rather than free-text only.

## Non-negotiable rules
- Never duplicate serial identity across products/locations.
- Warranty must reference the exact sold serial where the product is serial-controlled.

## Acceptance criteria
- Serial receive → sell → install → service.
- Return then resale policy.
- Warranty lookup.
- Accessory compatibility lookup.

---

# SKILL 18 — FURNITURE, STATIONERY & GENERAL MERCHANDISE

## Trigger
Use when adapting catalogue, units, variants, bundles, delivery, or pricing for non-serialized general goods.

## Objective
Support fast-moving stationery and configurable furniture/general merchandise without creating separate inventory engines.

## Microsoft technologies
- Catalogue module
- inventory module
- price engine
- unit conversions

## Implementation instructions
- Support carton/box/piece and similar conversions with one canonical base quantity.
- Support furniture variants such as size, color, material, finish, and optional assembly/delivery requirements.
- Allow bundle offers as sales pricing/grouping constructs without introducing manufacturing/BOM functionality.
- Support reorder levels and preferred supplier information.
- Allow barcode per sellable unit/variant where needed.

## Non-negotiable rules
- Bundles must not become hidden BOM/manufacturing constructs.
- Variant stock must remain independently traceable when variants are stocked separately.

## Acceptance criteria
- Carton-to-piece sale.
- Furniture color variant.
- Bundle pricing.
- Reorder alert.

---

# SKILL 19 — CRM, CUSTOMER CREDIT, RECEIVABLES & COLLECTIONS

## Trigger
Use for customer accounts, contacts, credit limits, ageing, statements, debt collection, or account history.

## Objective
Give retail and institutional businesses a complete customer financial and relationship view.

## Microsoft technologies
- Customer module
- accounting receivables
- Blazor/MAUI views
- reporting services

## Implementation instructions
- Support Walk-in customer plus registered Individual, Business, Government, NGO, Hospital/Clinic, School, and other configurable organization types.
- Institutional customers may have multiple contacts: procurement, finance, technical/biomedical, management.
- Credit profile includes configurable limit, terms, hold status, override permissions, and notes.
- Age receivables by due date and configurable buckets; show original invoice, payments, balance, days overdue, and branch.
- Generate customer statement with opening balance, invoices, receipts, adjustments, and closing balance.
- Credit-limit overrides require authorized approval and audit evidence.

## Non-negotiable rules
- Credit is not a payment method.
- Do not calculate receivable balance from a mutable customer balance field as the only source of truth.

## Acceptance criteria
- Partial payment.
- Ageing.
- Credit-limit warning/block.
- Customer statement.
- Institution with multiple contacts.

---

# SKILL 20 — INSTALLATION, WARRANTY, SERVICE & MAINTENANCE

## Trigger
Use for delivery installation, commissioning, service tickets, repairs, parts used, preventive maintenance, or warranties.

## Objective
Extend equipment sales into complete after-sales lifecycle management.

## Microsoft technologies
- Service module
- MAUI for field workflows
- attachments
- serial inventory integration

## Implementation instructions
- Installation job references customer, site, delivery/sale, serial units, assigned engineer, checklist, date/time, notes, acceptance, and optional evidence attachments.
- Warranty records start/end, warranty type, supplier warranty, customer warranty, covered serial/product, and claim/service links.
- ServiceTicket captures customer, site, equipment/serial, problem, priority, assignment, diagnosis, labor, parts, status, resolution, and customer acceptance.
- Parts consumed by a service job create normal inventory movements from the correct service/warehouse stock location.
- Preventive maintenance schedules may create planned service jobs without turning the module into manufacturing.
- MAUI field screens should work offline and sync evidence/results later.

## Non-negotiable rules
- Parts usage must affect inventory once only.
- Service history on a serial unit is append-only/auditable.
- Warranty coverage decisions should be explicit and configurable.

## Acceptance criteria
- Offline field service.
- Warranty claim.
- Parts consumption.
- Installation completion.
- Preventive maintenance generation.

---

# SKILL 21 — REPORTING, DASHBOARDS, DRILL-DOWN & SIGNALR

## Trigger
Use for reports, owner dashboards, financial statements, stock analysis, live operational updates, or export.

## Objective
Provide Tally-like drill-down depth with modern management dashboards and reliable server-side calculations.

## Microsoft technologies
- Blazor
- SignalR
- EF Core projections
- SQL Server read queries
- Razor components

## Implementation instructions
- Core reports: Day Book/Transaction Register, Sales, Purchases, Stock Summary, Stock Movement, Inventory Valuation, Serial Register, Batch/Expiry, Receivables, Payables, Cash/Bank/Cheque registers, Trial Balance, P&L, Balance Sheet, Cash Flow, Tender Pipeline, Warranty/Service.
- Every summarized figure should drill to the underlying period/category/document/transaction where permissions permit.
- Use server-side filtering, paging, grouping, and projection for large datasets.
- For online users, SignalR may push changed KPIs/approvals/stock notifications; always retain refresh/poll fallback.
- Owner dashboard should foreground sales, gross profit, cash position, bank position, receivables, payables, low stock, overdue debt, and branch comparison.
- Use database views/read models only when their ownership and refresh rules are explicit.

## Non-negotiable rules
- Financial report numbers must reconcile to the accounting ledger.
- Stock reports must reconcile to inventory movements.
- Dashboards are not sources of truth.

## Acceptance criteria
- Drill-down integrity.
- P&L reconciliation.
- Stock summary reconciliation.
- Large dataset pagination.
- SignalR reconnect.

---

# SKILL 22 — AUDIT TRAIL, FRAUD CONTROLS & APPROVAL WORKFLOWS

## Trigger
Use for audit logs, suspicious activity, approval limits, voids, discounts, price changes, stock adjustments, or compliance controls.

## Objective
Make high-risk business activity transparent and resistant to silent manipulation.

## Microsoft technologies
- ASP.NET Core authorization
- append-only audit records
- structured logging
- approval domain services

## Implementation instructions
- Record actor, device, branch, action, entity/document, before/after summary where appropriate, time, reason, correlation ID, and approval reference.
- Flag or require approval for configurable events such as large discount, below-cost sale, stock adjustment, back-dated posting, high-value refund, void, repeated cashier cancellations, credit-limit override, and cheque status override.
- Create ApprovalRequest with requested action, requester, approver, thresholds, reason, decision, and timestamps.
- Protect audit records from ordinary application deletion. Export/read access is permission-controlled.
- Use anomaly rules as transparent business controls; do not label staff fraudulent solely from an automated flag.

## Non-negotiable rules
- No administrator can silently erase a posted transaction or its audit evidence through the normal UI.
- Approval cannot be self-approved when policy prohibits it.

## Acceptance criteria
- Discount approval.
- Stock adjustment approval.
- Void audit.
- Back-date flag.
- Audit export permissions.

---

# SKILL 23 — SIERRA LEONE CONFIGURATION & COMPLIANCE LAYER

## Trigger
Use for currency, tax/GST, document numbering, business registration identifiers, fiscal integrations, or country-specific settings.

## Objective
Keep Sierra Leone localization first-class while ensuring legal/rate changes are configuration updates rather than code rewrites.

## Microsoft technologies
- Options/configuration services
- effective-dated reference tables
- integration adapters

## Implementation instructions
- Default functional currency to SLE/NLe with configurable display precision and currency symbol/text.
- Create effective-dated TaxCode/TaxRate configuration. Do not hard-code a current GST percentage into business logic.
- Model company identifiers and invoice fields as configurable metadata so statutory changes can be adopted.
- Create an `IFiscalIntegration`/adapter boundary for any NRA/ECR/fiscal-device integration so core sales logic is not vendor/protocol-specific.
- Keep cheque/bank workflows aligned with the agreed product scope.
- Localize dates, money, addresses, phone formats, and print layouts through configuration/resources rather than forks.

## Non-negotiable rules
- Any statutory claim or integration must be verified against current official requirements before production release.
- Changing a future rate must not require recompiling the accounting engine.

## Acceptance criteria
- Effective-date tax change.
- Historical invoice retains old rate.
- Currency formatting.
- Fiscal connector disabled/unavailable behavior.

---

# SKILL 24 — DOCUMENTS, PRINTING, RECEIPTS & ATTACHMENTS

## Trigger
Use for invoices, receipts, quotations, statements, delivery notes, service certificates, printing, PDFs, or evidence files.

## Objective
Produce professional business documents locally and centrally while abstracting printer/storage providers.

## Microsoft technologies
- Razor/HTML templates or approved .NET document library
- MAUI platform printing abstraction
- Azure Blob optional
- local file storage abstraction

## Implementation instructions
- Define document templates for POS receipt, tax/business invoice, quotation, sales order, delivery note, customer statement, purchase order, cheque receipt, bank-transfer receipt, installation certificate, service report, tender costing/output.
- Generate deterministic document numbers from configurable sequences with offline-safe partitioning (for example branch/device prefixes) where needed.
- Store attachments through `IDocumentStorage` so local filesystem, network storage, or Azure Blob can be selected by deployment.
- Keep metadata in SQL: owner document ID, file name, media type, size, hash, storage key, uploader, date, classification.
- For offline evidence, save locally first and upload through the sync/outbox mechanism.
- Printing failures must not roll back an already committed sale; allow reprint with audit markers.

## Non-negotiable rules
- Do not store huge binary attachments directly in core transactional tables unless explicitly chosen for a small deployment.
- Reprints and replaced documents must remain traceable.

## Acceptance criteria
- Offline receipt print.
- Invoice PDF generation.
- Attachment sync.
- Reprint after printer failure.
- Access-control test.

---

# SKILL 25 — BACKGROUND WORKERS, RESILIENCE & OBSERVABILITY

## Trigger
Use for scheduled/background work, sync processing, reminders, cleanup, health checks, retries, diagnostics, or production support.

## Objective
Make the system recoverable and diagnosable under unstable networks, service restarts, and branch outages.

## Microsoft technologies
- .NET Worker Service
- BackgroundService
- HttpClientFactory
- Microsoft.Extensions.Logging
- health checks

## Implementation instructions
- Use background workers for sync dispatch, attachment upload, server-side report jobs where needed, reminder generation, and maintenance—not for business operations that must complete before user confirmation.
- All jobs must be idempotent and record execution state/checkpoints.
- Use structured logs with CompanyId, BranchId, DeviceId, UserId where appropriate, CorrelationId, OperationId, and document number.
- Add health endpoints for database, storage, and critical dependency status in central deployments.
- Apply bounded retry/backoff to transient network failures; do not retry permanent validation errors indefinitely.
- Provide an administrator diagnostics screen for sync failures and environment/version information without exposing secrets.

## Non-negotiable rules
- Logs must not expose passwords, tokens, full bank credentials, or unnecessary sensitive data.
- Background failure must not silently lose work.

## Acceptance criteria
- Retry then recovery.
- Permanent failure dead-letter/diagnostic path.
- Worker restart.
- Health-check degradation.

---

# SKILL 26 — DATABASE MIGRATIONS, VERSIONING & SAFE UPGRADES

## Trigger
Use when changing schemas, releasing a client/server version, upgrading local SQLite, or changing sync contracts.

## Objective
Allow installed branch/POS clients and the central system to evolve without data loss or incompatible synchronization.

## Microsoft technologies
- EF Core migrations
- application schema version
- sync contract version
- SQL backup/restore procedures

## Implementation instructions
- Every schema change is represented by reviewed EF Core migrations for relevant providers.
- Central deployment migrates in a controlled step before incompatible application code is activated.
- MAUI client checks local schema version on startup and upgrades transactionally with backup/recovery strategy appropriate to the change.
- Maintain a client/server compatibility matrix for sync protocol versions.
- Prefer additive API/schema changes before removals. Deprecate fields/contracts over at least one supported upgrade path where feasible.
- Never run destructive production migrations without tested backups and an explicit data transformation/recovery plan.

## Non-negotiable rules
- No manual production schema edits that are absent from source control/migrations.
- Client must refuse unsafe incompatible sync with a clear upgrade message rather than corrupt data.

## Acceptance criteria
- Upgrade from previous release.
- Rollback/recovery rehearsal.
- Old client against new server compatibility.
- SQLite migration with existing unsynced outbox.

---

# SKILL 27 — TESTING & QUALITY ENGINEERING

## Trigger
Use for any feature completion, bug fix, release, refactor, or risky data change.

## Objective
Protect the four highest-risk areas—accounting, inventory, sync, and payments—with automated tests and realistic workflows.

## Microsoft technologies
- xUnit or approved .NET test framework
- ASP.NET Core WebApplicationFactory
- EF Core SQLite test databases
- SQL Server integration environment
- MAUI/Blazor UI test tooling where practical

## Implementation instructions
- Unit-test domain invariants and posting rules without database dependency where possible.
- Integration-test EF Core mappings, transactions, unique constraints, SQL Server behavior, and SQLite behavior.
- Use API tests for authorization, idempotency, validation, and business workflows.
- Create sync simulations that deliberately duplicate, reorder, delay, and interrupt messages.
- Create golden accounting scenarios with expected journals and statements.
- Create inventory scenarios with purchase, transfer, sale, return, stock count, serial, batch, and Excel import.
- Test offline workflows before calling a feature complete.
- Every production bug in a critical path should add a regression test.

## Non-negotiable rules
- Do not mock away the behavior being tested.
- Do not use EF Core InMemory provider as proof that relational constraints/transactions work; prefer SQLite or real SQL Server depending on purpose.

## Acceptance criteria
- All critical suites pass in CI.
- No failing migration test.
- No unbalanced journal scenario.
- No duplicate sync transaction.
- Import validation regression coverage.

---

# SKILL 28 — DEPLOYMENT, SQL SERVER & AZURE/ON-PREMISE OPERATIONS

## Trigger
Use for production hosting, branch installations, database editions, cloud deployment, backups, environments, CI/CD, or release packaging.

## Objective
Support both local/on-premise and cloud-connected deployments without changing the business architecture.

## Microsoft technologies
- SQL Server 2025 Express/Standard
- Azure SQL optional
- Azure App Service optional
- Azure Blob optional
- GitHub Actions/Azure DevOps
- .NET publish

## Implementation instructions
- For small central/on-prem deployments, support SQL Server 2025 Express within its licensed/capacity constraints; larger deployments can use Standard or Azure SQL.
- Publish API/Web as versioned artifacts. Keep environment configuration external to binaries and protect secrets through proper secret stores/environment configuration.
- Windows MAUI release must have a controlled package/update process and rollback plan.
- Automate backups for central SQL Server and attachment storage; periodically test restoration.
- Separate Development, Test/Staging, and Production configuration and databases.
- Run database migrations as a controlled deployment step, not independently from every app instance racing at startup.
- Expose version/build information in diagnostics and sync handshake.

## Non-negotiable rules
- Azure must not be required for offline branch operation.
- Do not put production secrets in appsettings files committed to source control.
- A release is not complete until backup/restore and migration implications are considered.

## Acceptance criteria
- Fresh install.
- Upgrade install.
- Backup/restore.
- Offline branch during central outage.
- Azure and on-prem configuration validation.

---

# IMPLEMENTATION ROADMAP

The AI coding agent should normally implement in the following order unless an existing repository makes another sequence safer.

## Phase 0 — Engineering foundation
- Create solution/project structure.
- Add central dependency/version management.
- Configure analyzers, nullable reference types, warnings, formatting, and CI build.
- Establish Domain/Application/Infrastructure boundaries.
- Create SQL Server and SQLite development databases.
- Add base EF Core migration pipeline.
- Add logging, configuration, error handling, and health foundations.

**Exit condition:** solution builds from clean checkout; central and local databases can be created from migrations.

## Phase 1 — Organization, identity, catalogue
- Company, branch, stock locations.
- ASP.NET Core Identity.
- Roles/permissions.
- Product/category/unit/price/barcode master data.
- Shared Tally-inspired UI shell and Go To navigation.
- MAUI SQLite lifecycle.

**Exit condition:** authenticated user can work in assigned company/branch, create catalogue data, and use both MAUI and Web shells.

## Phase 2 — Inventory and Excel onboarding
- Inventory movement engine.
- Stock projections.
- Warehouses/transfers.
- Serial and batch/expiry.
- Stock count/adjustment.
- Excel inventory import wizard using Open XML SDK.

**Exit condition:** opening stock can be loaded from Excel and reconciles exactly to Stock Summary by location and tracking mode.

## Phase 3 — Retail and sales
- Retail POS.
- Price levels/quantity pricing.
- Cash/cheque/bank-transfer capture.
- Sales invoice/order/delivery workflows.
- Customer credit/receivables.
- Returns and cashier closing.

**Exit condition:** a branch can trade for a full day without internet, print documents, and retain all transactions locally.

## Phase 4 — Purchasing and accounting
- Suppliers and purchasing.
- Foreign currency.
- Import/landed cost.
- Chart of accounts.
- Automated posting engine.
- Trial Balance, P&L, Balance Sheet.
- Bank and cheque reconciliation.

**Exit condition:** every supported sale/purchase/payment/return creates tested balanced journal entries and financial statements reconcile.

## Phase 5 — Synchronization and multi-branch
- Local outbox/inbox.
- Central sync endpoints.
- Bootstrap and incremental pull.
- Conflict queue.
- Attachment sync.
- Sync diagnostics.

**Exit condition:** two or more offline branch/devices can transact, reconnect, synchronize idempotently, and produce consistent central stock/accounting results.

## Phase 6 — Institutional and equipment workflows
- Quotations.
- Tenders.
- Institutional sales orders.
- Serialized equipment lifecycle.
- Installation/commissioning.
- Warranty.
- Service/repair.

**Exit condition:** a medical/IT equipment transaction can be traced from purchase/serial through quote/order/sale/delivery/installation/warranty/service.

## Phase 7 — Management, controls and production readiness
- Dashboards and drill-down reports.
- Audit/fraud controls and approvals.
- Sierra Leone configuration/compliance adapter layer.
- Production document templates.
- Backup/restore.
- Deployment packaging.
- Load/performance testing.
- Security review.

**Exit condition:** production release meets the Definition of Done and can be deployed on-premise or cloud-connected without changing domain code.

---

# AI CODING AGENT EXECUTION PROTOCOL

For every requested implementation, the coding agent should follow this sequence:

1. **Identify the owning module and relevant skill(s).**
2. **Inspect the existing repository first.** Do not create parallel architectures, duplicate services, or replacement schemas when equivalent structures already exist.
3. **State the invariants affected**—especially stock, accounting, payment, serial, sync, and permissions.
4. **Design the smallest coherent change** across Domain → Application → Infrastructure → API/UI.
5. **Add or update EF migrations** only when persistence changes are necessary.
6. **Implement server-side/domain authorization and validation**, not only UI checks.
7. **Preserve offline behavior.** If the change is a core transaction, verify how it works in local SQLite and through sync.
8. **Preserve idempotency.** Any retried import/sync/external write must not duplicate business effects.
9. **Add tests before declaring completion.** Critical bugs require regression tests.
10. **Run at least:** `dotnet restore`, `dotnet build`, relevant `dotnet test`, migration validation, and the affected workflow tests.
11. **Review for forbidden scope:** no manufacturing/BOM, and no payment methods other than cash, cheque, bank transfer unless explicitly changed.
12. **Document user-visible changes, migrations, configuration keys, and deployment implications.**

### When modifying accounting
Always show or test the intended debit/credit effect and reversal behavior.

### When modifying inventory
Always show or test the stock movement effect, location, unit conversion, serial/batch behavior, and reversal behavior.

### When modifying sync
Always test retry, duplication, ordering, offline interruption, and version compatibility.

### When modifying Excel import
Always test mapping, validation, preview, duplicate detection, idempotency, and rollback/corrective reversal behavior.

---

# DEFINITION OF DONE

A feature is not complete merely because a screen renders. It is complete only when all applicable conditions below are met.

## Functional
- Required workflow works in the intended Retail / Wholesale / Distribution / Institutional context.
- Validation and meaningful error messages are present.
- Permissions are enforced server-side/application-side.
- Audit events exist for material changes.

## Offline
- Core transactional workflow works with the network disconnected when applicable.
- Local SQLite commit is durable.
- Unsynced work is visible in sync diagnostics.
- Reconnection does not duplicate the transaction.

## Inventory
- Correct InventoryMovement records are produced.
- Stock balance/projection reconciles to movements.
- Serial/batch/expiry rules are enforced when applicable.
- Reversal/return behavior is tested.

## Accounting
- Correct JournalEntry/JournalLine records are produced.
- Debits equal credits.
- Receivable/payable/cash/bank balances reconcile.
- Correction uses reversal/amendment rather than deletion.

## Data
- EF Core migrations are source-controlled and tested.
- SQL Server and SQLite provider differences are considered.
- Monetary precision and constraints are correct.

## UI
- Shared Razor components are reused where appropriate.
- Keyboard-first desktop flow is preserved.
- Mouse/touch alternatives exist.
- Loading, empty, offline, error, permission-denied, and success states are designed.

## Security
- No secrets in source control.
- Authentication/authorization tests cover sensitive actions.
- File uploads/imports are validated.
- Logs avoid leaking sensitive values.

## Quality
- Relevant unit, integration, API, sync, accounting, and inventory tests pass.
- Clean `dotnet build` succeeds.
- Critical warnings are resolved or explicitly justified.
- No unexplained regression in existing modules.

## Release
- Configuration changes are documented.
- Database migration impact is documented.
- Backup/restore implications are considered.
- Client/server compatibility is stated for sync-affecting changes.

---

# PRODUCT SCOPE SUMMARY

## In scope
- Retail POS
- Wholesale
- Distribution
- Institutional sales
- Quotations
- Tenders
- Product catalogue
- Warehouses/branches
- Quantity inventory
- Serial numbers
- Batch/lot and expiry
- Variants and compatibility
- Excel inventory upload/import
- Purchases and imports
- Landed cost
- Cash
- Cheques
- Bank transfers
- Customer credit and receivables
- Double-entry accounting
- Equipment installation/commissioning
- Warranty
- Service and repairs
- Reporting and drill-down
- Audit and approvals
- Offline operation
- Multi-device/multi-branch synchronization
- Windows, Web, and Android clients using shared .NET/Razor assets

## Explicitly out of scope unless later requested
- Manufacturing
- Bill of Materials (BOM)
- Production planning
- Mobile-money payments
- Card payments
- Cryptocurrency payments

---

# FINAL BUILD PRINCIPLE

The system should feel **simple to a cashier or trader, precise to an accountant, controllable to an owner, and resilient to Sierra Leone connectivity conditions**.

The preferred technical expression of that principle is:

```text
C# 14 + .NET 10 LTS
        │
        ├── .NET MAUI Blazor Hybrid ── SQLite ── Offline Outbox
        │
        ├── Blazor Web App
        │
        └── ASP.NET Core API
                    │
              EF Core 10
                    │
             SQL Server 2025
                    │
        Optional Azure hosting/storage
```

The business rules belong in shared .NET domain/application code. The UI is a client of those rules. SQLite preserves branch continuity. SQL Server provides central consistency. Synchronization connects the two without making the business dependent on continuous internet access.
