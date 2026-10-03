# Skill acceptance checklist

Audit date: 2026-09-27. Extracted verbatim from every Acceptance criteria section in the supplied skill pack. See feature-audit.md for source evidence, defects and boundaries. PASS means the stated subset has test evidence; it does not sign off its whole skill. NOT MET includes absent or incomplete workflows. UNVERIFIED means the required execution boundary has not been tested.

## SKILL 1 — MICROSOFT SOLUTION ARCHITECTURE & MODULE BOUNDARIES

| Acceptance criterion | Audit result |
|---|---|
| Solution builds from `dotnet build`. | PARTIAL — shared solution builds/dependencies inspected; full architecture remains incomplete |
| Module dependencies are documented. | PARTIAL — shared solution builds/dependencies inspected; full architecture remains incomplete |
| No circular project references. | PARTIAL — shared solution builds/dependencies inspected; full architecture remains incomplete |
| Desktop and Web clients consume the same application contracts. | PARTIAL — shared solution builds/dependencies inspected; full architecture remains incomplete |
## SKILL 2 — DOMAIN MODEL, EF CORE 10 & SQL SERVER DESIGN

| Acceptance criterion | Audit result |
|---|---|
| Migrations apply to a clean SQL Server database. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| SQLite local database initializes from a versioned migration path. | PASS — real SQLite migration tests |
| Uniqueness, foreign keys, money precision, and concurrency rules are tested. | PARTIAL — SQLite coverage; SQL Server/complete precision matrix absent |
## SKILL 3 — OFFLINE-FIRST SQLITE & SYNCHRONIZATION ENGINE

| Acceptance criterion | Audit result |
|---|---|
| Offline sale then reconnect sync. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Duplicate event delivery. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Power interruption after local commit but before upload. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Two offline devices selling stock. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Server unavailable for extended periods. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Schema-version mismatch handling. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 4 — TALLY-INSPIRED SHARED UI DESIGN SYSTEM

| Acceptance criterion | Audit result |
|---|---|
| Gateway renders in MAUI and Web. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| Keyboard navigation completes a sale without mouse. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| Screen-reader labels and focus order are reasonable. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| 1024px tablet layout remains usable. | UNVERIFIED — required UI/device/deployment boundary not exercised |
## SKILL 5 — .NET MAUI BLAZOR HYBRID DESKTOP / POS CLIENT

| Acceptance criterion | Audit result |
|---|---|
| Windows offline startup. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| Local DB upgrade. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| Barcode scanner keyboard-wedge flow. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Printer unavailable handling. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Android shared screen rendering. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 6 — BLAZOR WEB MANAGEMENT PORTAL

| Acceptance criterion | Audit result |
|---|---|
| Unauthorized URL access denied. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| Shared components render consistently. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| Live update reconnect works. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Large report pagination/filtering performs acceptably. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 7 — ASP.NET CORE API, APPLICATION SERVICES & CONTRACTS

| Acceptance criterion | Audit result |
|---|---|
| Idempotency test. | PASS — repeated command/collision tests |
| Validation and authorization tests. | PARTIAL — service tests pass; complete HTTP suite absent |
| Backward compatibility test for sync contract. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| ProblemDetails consistency. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 8 — IDENTITY, ROLES & PERMISSION-BASED AUTHORIZATION

| Acceptance criterion | Audit result |
|---|---|
| Permission matrix tests. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Cross-branch access denial. | PASS — application service test |
| Offline cached permission expiry. | PASS — desktop local session expiry test |
| Disabled-user sync behavior. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 9 — PRODUCT CATALOGUE, INVENTORY, SERIALS, BATCHES & WAREHOUSES

| Acceptance criterion | Audit result |
|---|---|
| Serial lifecycle. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Batch FEFO. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Multi-unit purchase/sale. | PASS — explicit conversion test |
| Transfer between locations. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Concurrent sale protection. | PASS — competing SQLite sale test |
| Stock count adjustment. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 10 — EXCEL INVENTORY IMPORT & BULK STOCK ONBOARDING

| Acceptance criterion | Audit result |
|---|---|
| 1,000+ product import. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Duplicate serial rejection. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Opening stock by multiple warehouses. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Batch/expiry import. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Malformed workbook. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Retry committed import. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Error workbook generation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 11 — RETAIL POS & CASHIER OPERATIONS

| Acceptance criterion | Audit result |
|---|---|
| Keyboard-only sale. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| Offline sale. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| Cash sale. | PASS — settlement and reversal integration test |
| Cheque sale. | PASS — ChequesInHand posting and reversal test (not clearance) |
| Bank-transfer sale. | PASS — Bank posting and reversal test (not reconciliation) |
| Serial sale. | PASS — serial stock integration test |
| Return/exchange. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Cash drawer variance. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 12 — SALES, WHOLESALE, DISTRIBUTION & INSTITUTIONAL ORDERS

| Acceptance criterion | Audit result |
|---|---|
| Wholesale quantity pricing. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Partial delivery. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Sales order reservation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Institutional PO reference. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Credit invoice then later payment allocation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 13 — PURCHASING, IMPORTS & LANDED COST

| Acceptance criterion | Audit result |
|---|---|
| Foreign-currency purchase. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Partial goods receipt. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Landed-cost allocation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Supplier payment. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Purchase return. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 14 — DOUBLE-ENTRY ACCOUNTING ENGINE

| Acceptance criterion | Audit result |
|---|---|
| Balanced-entry invariant. | PASS — operational posting tests |
| Sale posting. | PASS — stock/COGS/settlement tests |
| Credit sale/payment allocation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Cheque lifecycle postings. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Return reversal. | PASS — full-document reversal only |
| Closed period rejection. | PASS — configured application-service date lock |
| Trial balance totals. | PASS — service ledger totals, not UI |
## SKILL 15 — CASH, CHEQUE & BANK TRANSFER MANAGEMENT

| Acceptance criterion | Audit result |
|---|---|
| Partial invoice payment. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| One receipt allocated to several invoices. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Cheque clear. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Cheque bounce. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Bank transfer reconciliation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Cash shift totals. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 16 — QUOTATIONS, TENDERS & INSTITUTIONAL PROCUREMENT SALES

| Acceptance criterion | Audit result |
|---|---|
| Tender creation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Bid revision/version. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Approval. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Won tender conversion. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Lost tender closure. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Attachment authorization. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 17 — MEDICAL & IT EQUIPMENT SERIAL LIFECYCLE

| Acceptance criterion | Audit result |
|---|---|
| Serial receive → sell → install → service. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Return then resale policy. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Warranty lookup. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Accessory compatibility lookup. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 18 — FURNITURE, STATIONERY & GENERAL MERCHANDISE

| Acceptance criterion | Audit result |
|---|---|
| Carton-to-piece sale. | PASS — base-unit conversion integration test |
| Furniture color variant. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Bundle pricing. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Reorder alert. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 19 — CRM, CUSTOMER CREDIT, RECEIVABLES & COLLECTIONS

| Acceptance criterion | Audit result |
|---|---|
| Partial payment. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Ageing. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Credit-limit warning/block. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Customer statement. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Institution with multiple contacts. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 20 — INSTALLATION, WARRANTY, SERVICE & MAINTENANCE

| Acceptance criterion | Audit result |
|---|---|
| Offline field service. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Warranty claim. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Parts consumption. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Installation completion. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Preventive maintenance generation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 21 — REPORTING, DASHBOARDS, DRILL-DOWN & SIGNALR

| Acceptance criterion | Audit result |
|---|---|
| Drill-down integrity. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| P&L reconciliation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Stock summary reconciliation. | PASS — quantity/value assertions against trade scenario |
| Large dataset pagination. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| SignalR reconnect. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 22 — AUDIT TRAIL, FRAUD CONTROLS & APPROVAL WORKFLOWS

| Acceptance criterion | Audit result |
|---|---|
| Discount approval. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Stock adjustment approval. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Void audit. | PARTIAL — reversal logs exist; audit protection gaps documented |
| Back-date flag. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Audit export permissions. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 23 — SIERRA LEONE CONFIGURATION & COMPLIANCE LAYER

| Acceptance criterion | Audit result |
|---|---|
| Effective-date tax change. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Historical invoice retains old rate. | PASS — added tax-retention integration test |
| Currency formatting. | PARTIAL — fixed NLe/two decimals; configurable localization absent |
| Fiscal connector disabled/unavailable behavior. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 24 — DOCUMENTS, PRINTING, RECEIPTS & ATTACHMENTS

| Acceptance criterion | Audit result |
|---|---|
| Offline receipt print. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Invoice PDF generation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Attachment sync. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Reprint after printer failure. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Access-control test. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 25 — BACKGROUND WORKERS, RESILIENCE & OBSERVABILITY

| Acceptance criterion | Audit result |
|---|---|
| Retry then recovery. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Permanent failure dead-letter/diagnostic path. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Worker restart. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Health-check degradation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 26 — DATABASE MIGRATIONS, VERSIONING & SAFE UPGRADES

| Acceptance criterion | Audit result |
|---|---|
| Upgrade from previous release. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Rollback/recovery rehearsal. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| Old client against new server compatibility. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| SQLite migration with existing unsynced outbox. | PASS — migration replay with operation log |
## SKILL 27 — TESTING & QUALITY ENGINEERING

| Acceptance criterion | Audit result |
|---|---|
| All critical suites pass in CI. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| No failing migration test. | PASS — SQLite suite and model-drift check |
| No unbalanced journal scenario. | PASS — tested operational scenarios only |
| No duplicate sync transaction. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Import validation regression coverage. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
## SKILL 28 — DEPLOYMENT, SQL SERVER & AZURE/ON-PREMISE OPERATIONS

| Acceptance criterion | Audit result |
|---|---|
| Fresh install. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| Upgrade install. | UNVERIFIED — required UI/device/deployment boundary not exercised |
| Backup/restore. | PASS — independent restored database integration test |
| Offline branch during central outage. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |
| Azure and on-prem configuration validation. | NOT MET — missing/incomplete; see corresponding skill in feature-audit.md |

Total acceptance criteria retained: 139 across 28 skills. No entire skill is signed off.
