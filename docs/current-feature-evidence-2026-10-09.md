# Current criterion evidence index — 9 October 2026

All 139 original criteria are retained below. This index maps each criterion to current implementation/test sources; it is not a claim that every criterion was executed or passed. Test class references are under tests/SierraBusiness.IntegrationTests. Review the actual test scenarios and 0.31.0 TRX before acceptance. The historical September results remain unchanged in feature-acceptance-checklist.md. External integrations are deferred; Android and manufacturing remain excluded.


## SKILL 1 — MICROSOFT SOLUTION ARCHITECTURE & MODULE BOUNDARIES

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Solution builds from `dotnet build`. | Implementation / automated scenario evidence; review exact coverage | SierraBusiness.sln; docs/engineering-specification.md |
| Module dependencies are documented. | Implementation / automated scenario evidence; review exact coverage | SierraBusiness.sln; docs/engineering-specification.md |
| No circular project references. | Implementation / automated scenario evidence; review exact coverage | SierraBusiness.sln; docs/engineering-specification.md |
| Desktop and Web clients consume the same application contracts. | Implementation / automated scenario evidence; review exact coverage | SierraBusiness.sln; docs/engineering-specification.md |

## SKILL 2 — DOMAIN MODEL, EF CORE 10 & SQL SERVER DESIGN

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Migrations apply to a clean SQL Server database. | Provider implemented; live connection acceptance pending | SqlServerTests, PostgresTests, IntegrityTests, UpgradeTests |
| SQLite local database initializes from a versioned migration path. | Implementation / automated scenario evidence; review exact coverage | SqlServerTests, PostgresTests, IntegrityTests, UpgradeTests |
| Uniqueness, foreign keys, money precision, and concurrency rules are tested. | Implementation / automated scenario evidence; review exact coverage | SqlServerTests, PostgresTests, IntegrityTests, UpgradeTests |

## SKILL 3 — OFFLINE-FIRST SQLITE & SYNCHRONIZATION ENGINE

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Offline sale then reconnect sync. | Implementation / automated scenario evidence; review exact coverage | SyncTests, SyncCompatibilityTests, DesktopTests |
| Duplicate event delivery. | Implementation / automated scenario evidence; review exact coverage | SyncTests, SyncCompatibilityTests, DesktopTests |
| Power interruption after local commit but before upload. | Native / hardware / deployed / CI acceptance pending; automated subset only | SyncTests, SyncCompatibilityTests, DesktopTests |
| Two offline devices selling stock. | Native / hardware / deployed / CI acceptance pending; automated subset only | SyncTests, SyncCompatibilityTests, DesktopTests |
| Server unavailable for extended periods. | Native / hardware / deployed / CI acceptance pending; automated subset only | SyncTests, SyncCompatibilityTests, DesktopTests |
| Schema-version mismatch handling. | Implementation / automated scenario evidence; review exact coverage | SyncTests, SyncCompatibilityTests, DesktopTests |

## SKILL 4 — TALLY-INSPIRED SHARED UI DESIGN SYSTEM

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Gateway renders in MAUI and Web. | Native / hardware / deployed / CI acceptance pending; automated subset only | WorkspaceHeaderTests, NavigationTests, WorkspaceTests |
| Keyboard navigation completes a sale without mouse. | Native / hardware / deployed / CI acceptance pending; automated subset only | WorkspaceHeaderTests, NavigationTests, WorkspaceTests |
| Screen-reader labels and focus order are reasonable. | Native / hardware / deployed / CI acceptance pending; automated subset only | WorkspaceHeaderTests, NavigationTests, WorkspaceTests |
| 1024px tablet layout remains usable. | Native / hardware / deployed / CI acceptance pending; automated subset only | WorkspaceHeaderTests, NavigationTests, WorkspaceTests |

## SKILL 5 — .NET MAUI BLAZOR HYBRID DESKTOP / POS CLIENT

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Windows offline startup. | Native / hardware / deployed / CI acceptance pending; automated subset only | DesktopTests, WorkspaceTests, PrintableDocumentTests |
| Local DB upgrade. | Implementation / automated scenario evidence; review exact coverage | DesktopTests, WorkspaceTests, PrintableDocumentTests |
| Barcode scanner keyboard-wedge flow. | Native / hardware / deployed / CI acceptance pending; automated subset only | DesktopTests, WorkspaceTests, PrintableDocumentTests |
| Printer unavailable handling. | Native / hardware / deployed / CI acceptance pending; automated subset only | DesktopTests, WorkspaceTests, PrintableDocumentTests |
| Android shared screen rendering. | Excluded from current Windows scope | DesktopTests, WorkspaceTests, PrintableDocumentTests |

## SKILL 6 — BLAZOR WEB MANAGEMENT PORTAL

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Unauthorized URL access denied. | Implementation / automated scenario evidence; review exact coverage | ReportTests, ReportScreenTests; scripts/verify-web.ps1 |
| Shared components render consistently. | Implementation / automated scenario evidence; review exact coverage | ReportTests, ReportScreenTests; scripts/verify-web.ps1 |
| Live update reconnect works. | Authenticated HTTP/WebSocket reconnect verified by verify-web.ps1; installed network acceptance pending | ReportTests, ReportScreenTests; scripts/verify-web.ps1 |
| Large report pagination/filtering performs acceptably. | Implementation / automated scenario evidence; review exact coverage | ReportTests, ReportScreenTests; scripts/verify-web.ps1 |

## SKILL 7 — ASP.NET CORE API, APPLICATION SERVICES & CONTRACTS

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Idempotency test. | Implementation / automated scenario evidence; review exact coverage | TradingTests, SyncCompatibilityTests; scripts/verify-web.ps1 |
| Validation and authorization tests. | Implementation / automated scenario evidence; review exact coverage | TradingTests, SyncCompatibilityTests; scripts/verify-web.ps1 |
| Backward compatibility test for sync contract. | Implementation / automated scenario evidence; review exact coverage | TradingTests, SyncCompatibilityTests; scripts/verify-web.ps1 |
| ProblemDetails consistency. | Implementation / automated scenario evidence; review exact coverage | TradingTests, SyncCompatibilityTests; scripts/verify-web.ps1 |

## SKILL 8 — IDENTITY, ROLES & PERMISSION-BASED AUTHORIZATION

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Permission matrix tests. | Implementation / automated scenario evidence; review exact coverage | AdministrationAndPeriodTests, DesktopTests, SyncTests |
| Cross-branch access denial. | Implementation / automated scenario evidence; review exact coverage | AdministrationAndPeriodTests, DesktopTests, SyncTests |
| Offline cached permission expiry. | Implementation / automated scenario evidence; review exact coverage | AdministrationAndPeriodTests, DesktopTests, SyncTests |
| Disabled-user sync behavior. | Implementation / automated scenario evidence; review exact coverage | AdministrationAndPeriodTests, DesktopTests, SyncTests |

## SKILL 9 — PRODUCT CATALOGUE, INVENTORY, SERIALS, BATCHES & WAREHOUSES

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Serial lifecycle. | Implementation / automated scenario evidence; review exact coverage | InventoryTests, InventoryManagementTests, EquipmentTests |
| Batch FEFO. | Implementation / automated scenario evidence; review exact coverage | InventoryTests, InventoryManagementTests, EquipmentTests |
| Multi-unit purchase/sale. | Implementation / automated scenario evidence; review exact coverage | InventoryTests, InventoryManagementTests, EquipmentTests |
| Transfer between locations. | Implementation / automated scenario evidence; review exact coverage | InventoryTests, InventoryManagementTests, EquipmentTests |
| Concurrent sale protection. | Implementation / automated scenario evidence; review exact coverage | InventoryTests, InventoryManagementTests, EquipmentTests |
| Stock count adjustment. | Implementation / automated scenario evidence; review exact coverage | InventoryTests, InventoryManagementTests, EquipmentTests |

## SKILL 10 — EXCEL INVENTORY IMPORT & BULK STOCK ONBOARDING

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| 1,000+ product import. | Implementation / automated scenario evidence; review exact coverage | ImportTests, ImportScreenTests |
| Duplicate serial rejection. | Implementation / automated scenario evidence; review exact coverage | ImportTests, ImportScreenTests |
| Opening stock by multiple warehouses. | Implementation / automated scenario evidence; review exact coverage | ImportTests, ImportScreenTests |
| Batch/expiry import. | Implementation / automated scenario evidence; review exact coverage | ImportTests, ImportScreenTests |
| Malformed workbook. | Implementation / automated scenario evidence; review exact coverage | ImportTests, ImportScreenTests |
| Retry committed import. | Implementation / automated scenario evidence; review exact coverage | ImportTests, ImportScreenTests |
| Error workbook generation. | Implementation / automated scenario evidence; review exact coverage | ImportTests, ImportScreenTests |

## SKILL 11 — RETAIL POS & CASHIER OPERATIONS

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Keyboard-only sale. | Native / hardware / deployed / CI acceptance pending; automated subset only | PosTests, TradingTests, ReturnAndPartyTests, BankingTests |
| Offline sale. | Implementation / automated scenario evidence; review exact coverage | PosTests, TradingTests, ReturnAndPartyTests, BankingTests |
| Cash sale. | Implementation / automated scenario evidence; review exact coverage | PosTests, TradingTests, ReturnAndPartyTests, BankingTests |
| Cheque sale. | Implementation / automated scenario evidence; review exact coverage | PosTests, TradingTests, ReturnAndPartyTests, BankingTests |
| Bank-transfer sale. | Implementation / automated scenario evidence; review exact coverage | PosTests, TradingTests, ReturnAndPartyTests, BankingTests |
| Serial sale. | Implementation / automated scenario evidence; review exact coverage | PosTests, TradingTests, ReturnAndPartyTests, BankingTests |
| Return/exchange. | Implementation / automated scenario evidence; review exact coverage | PosTests, TradingTests, ReturnAndPartyTests, BankingTests |
| Cash drawer variance. | Implementation / automated scenario evidence; review exact coverage | PosTests, TradingTests, ReturnAndPartyTests, BankingTests |

## SKILL 12 — SALES, WHOLESALE, DISTRIBUTION & INSTITUTIONAL ORDERS

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Wholesale quantity pricing. | Implementation / automated scenario evidence; review exact coverage | OrderTests, CommerceTests, FinanceTests |
| Partial delivery. | Implementation / automated scenario evidence; review exact coverage | OrderTests, CommerceTests, FinanceTests |
| Sales order reservation. | Implementation / automated scenario evidence; review exact coverage | OrderTests, CommerceTests, FinanceTests |
| Institutional PO reference. | Implementation / automated scenario evidence; review exact coverage | OrderTests, CommerceTests, FinanceTests |
| Credit invoice then later payment allocation. | Implementation / automated scenario evidence; review exact coverage | OrderTests, CommerceTests, FinanceTests |

## SKILL 13 — PURCHASING, IMPORTS & LANDED COST

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Foreign-currency purchase. | Implementation / automated scenario evidence; review exact coverage | PurchasingTests, SupplierInvoiceTests, ReturnAndPartyTests |
| Partial goods receipt. | Implementation / automated scenario evidence; review exact coverage | PurchasingTests, SupplierInvoiceTests, ReturnAndPartyTests |
| Landed-cost allocation. | Implementation / automated scenario evidence; review exact coverage | PurchasingTests, SupplierInvoiceTests, ReturnAndPartyTests |
| Supplier payment. | Implementation / automated scenario evidence; review exact coverage | PurchasingTests, SupplierInvoiceTests, ReturnAndPartyTests |
| Purchase return. | Implementation / automated scenario evidence; review exact coverage | PurchasingTests, SupplierInvoiceTests, ReturnAndPartyTests |

## SKILL 14 — DOUBLE-ENTRY ACCOUNTING ENGINE

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Balanced-entry invariant. | Implementation / automated scenario evidence; review exact coverage | TradingTests, FinanceTests, BankingTests, AdministrationAndPeriodTests |
| Sale posting. | Implementation / automated scenario evidence; review exact coverage | TradingTests, FinanceTests, BankingTests, AdministrationAndPeriodTests |
| Credit sale/payment allocation. | Implementation / automated scenario evidence; review exact coverage | TradingTests, FinanceTests, BankingTests, AdministrationAndPeriodTests |
| Cheque lifecycle postings. | Implementation / automated scenario evidence; review exact coverage | TradingTests, FinanceTests, BankingTests, AdministrationAndPeriodTests |
| Return reversal. | Implementation / automated scenario evidence; review exact coverage | TradingTests, FinanceTests, BankingTests, AdministrationAndPeriodTests |
| Closed period rejection. | Implementation / automated scenario evidence; review exact coverage | TradingTests, FinanceTests, BankingTests, AdministrationAndPeriodTests |
| Trial balance totals. | Implementation / automated scenario evidence; review exact coverage | TradingTests, FinanceTests, BankingTests, AdministrationAndPeriodTests |

## SKILL 15 — CASH, CHEQUE & BANK TRANSFER MANAGEMENT

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Partial invoice payment. | Implementation / automated scenario evidence; review exact coverage | FinanceTests, BankingTests, TradeAdvanceTests |
| One receipt allocated to several invoices. | Implementation / automated scenario evidence; review exact coverage | FinanceTests, BankingTests, TradeAdvanceTests |
| Cheque clear. | Implementation / automated scenario evidence; review exact coverage | FinanceTests, BankingTests, TradeAdvanceTests |
| Cheque bounce. | Implementation / automated scenario evidence; review exact coverage | FinanceTests, BankingTests, TradeAdvanceTests |
| Bank transfer reconciliation. | Implementation / automated scenario evidence; review exact coverage | FinanceTests, BankingTests, TradeAdvanceTests |
| Cash shift totals. | Implementation / automated scenario evidence; review exact coverage | FinanceTests, BankingTests, TradeAdvanceTests |

## SKILL 16 — QUOTATIONS, TENDERS & INSTITUTIONAL PROCUREMENT SALES

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Tender creation. | Implementation / automated scenario evidence; review exact coverage | TenderTests, TenderScreenTests, DocumentDraftScreenTests |
| Bid revision/version. | Implementation / automated scenario evidence; review exact coverage | TenderTests, TenderScreenTests, DocumentDraftScreenTests |
| Approval. | Implementation / automated scenario evidence; review exact coverage | TenderTests, TenderScreenTests, DocumentDraftScreenTests |
| Won tender conversion. | Implementation / automated scenario evidence; review exact coverage | TenderTests, TenderScreenTests, DocumentDraftScreenTests |
| Lost tender closure. | Implementation / automated scenario evidence; review exact coverage | TenderTests, TenderScreenTests, DocumentDraftScreenTests |
| Attachment authorization. | Implementation / automated scenario evidence; review exact coverage | TenderTests, TenderScreenTests, DocumentDraftScreenTests |

## SKILL 17 — MEDICAL & IT EQUIPMENT SERIAL LIFECYCLE

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Serial receive → sell → install → service. | Implementation / automated scenario evidence; review exact coverage | EquipmentTests, ServiceTests, ReturnAndPartyTests |
| Return then resale policy. | Implementation / automated scenario evidence; review exact coverage | EquipmentTests, ServiceTests, ReturnAndPartyTests |
| Warranty lookup. | Implementation / automated scenario evidence; review exact coverage | EquipmentTests, ServiceTests, ReturnAndPartyTests |
| Accessory compatibility lookup. | Implementation / automated scenario evidence; review exact coverage | EquipmentTests, ServiceTests, ReturnAndPartyTests |

## SKILL 18 — FURNITURE, STATIONERY & GENERAL MERCHANDISE

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Carton-to-piece sale. | Implementation / automated scenario evidence; review exact coverage | CustomerMerchandiseTests, InventoryManagementTests, PosTests |
| Furniture color variant. | Implementation / automated scenario evidence; review exact coverage | CustomerMerchandiseTests, InventoryManagementTests, PosTests |
| Bundle pricing. | Implementation / automated scenario evidence; review exact coverage | CustomerMerchandiseTests, InventoryManagementTests, PosTests |
| Reorder alert. | Implementation / automated scenario evidence; review exact coverage | CustomerMerchandiseTests, InventoryManagementTests, PosTests |

## SKILL 19 — CRM, CUSTOMER CREDIT, RECEIVABLES & COLLECTIONS

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Partial payment. | Implementation / automated scenario evidence; review exact coverage | ReturnAndPartyTests, CustomerMerchandiseTests, FinanceTests |
| Ageing. | Implementation / automated scenario evidence; review exact coverage | ReturnAndPartyTests, CustomerMerchandiseTests, FinanceTests |
| Credit-limit warning/block. | Implementation / automated scenario evidence; review exact coverage | ReturnAndPartyTests, CustomerMerchandiseTests, FinanceTests |
| Customer statement. | Implementation / automated scenario evidence; review exact coverage | ReturnAndPartyTests, CustomerMerchandiseTests, FinanceTests |
| Institution with multiple contacts. | Implementation / automated scenario evidence; review exact coverage | ReturnAndPartyTests, CustomerMerchandiseTests, FinanceTests |

## SKILL 20 — INSTALLATION, WARRANTY, SERVICE & MAINTENANCE

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Offline field service. | Native / hardware / deployed / CI acceptance pending; automated subset only | ServiceTests, EquipmentTests |
| Warranty claim. | Implementation / automated scenario evidence; review exact coverage | ServiceTests, EquipmentTests |
| Parts consumption. | Implementation / automated scenario evidence; review exact coverage | ServiceTests, EquipmentTests |
| Installation completion. | Implementation / automated scenario evidence; review exact coverage | ServiceTests, EquipmentTests |
| Preventive maintenance generation. | Implementation / automated scenario evidence; review exact coverage | ServiceTests, EquipmentTests |

## SKILL 21 — REPORTING, DASHBOARDS, DRILL-DOWN & SIGNALR

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Drill-down integrity. | Implementation / automated scenario evidence; review exact coverage | ReportTests, ReportScreenTests; scripts/verify-web.ps1 |
| P&L reconciliation. | Implementation / automated scenario evidence; review exact coverage | ReportTests, ReportScreenTests; scripts/verify-web.ps1 |
| Stock summary reconciliation. | Implementation / automated scenario evidence; review exact coverage | ReportTests, ReportScreenTests; scripts/verify-web.ps1 |
| Large dataset pagination. | Implementation / automated scenario evidence; review exact coverage | ReportTests, ReportScreenTests; scripts/verify-web.ps1 |
| SignalR reconnect. | Authenticated HTTP/WebSocket reconnect verified by verify-web.ps1; installed network acceptance pending | ReportTests, ReportScreenTests; scripts/verify-web.ps1 |

## SKILL 22 — AUDIT TRAIL, FRAUD CONTROLS & APPROVAL WORKFLOWS

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Discount approval. | Implementation / automated scenario evidence; review exact coverage | ControlTests, IntegrityTests, RecordsSafetyTests |
| Stock adjustment approval. | Implementation / automated scenario evidence; review exact coverage | ControlTests, IntegrityTests, RecordsSafetyTests |
| Void audit. | Implementation / automated scenario evidence; review exact coverage | ControlTests, IntegrityTests, RecordsSafetyTests |
| Back-date flag. | Implementation / automated scenario evidence; review exact coverage | ControlTests, IntegrityTests, RecordsSafetyTests |
| Audit export permissions. | Implementation / automated scenario evidence; review exact coverage | ControlTests, IntegrityTests, RecordsSafetyTests |

## SKILL 23 — SIERRA LEONE CONFIGURATION & COMPLIANCE LAYER

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Effective-date tax change. | Implementation / automated scenario evidence; review exact coverage | ComplianceTests, AdministrationAndPeriodTests, PrintableDocumentTests |
| Historical invoice retains old rate. | Implementation / automated scenario evidence; review exact coverage | ComplianceTests, AdministrationAndPeriodTests, PrintableDocumentTests |
| Currency formatting. | Implementation / automated scenario evidence; review exact coverage | ComplianceTests, AdministrationAndPeriodTests, PrintableDocumentTests |
| Fiscal connector disabled/unavailable behavior. | Implementation / automated scenario evidence; review exact coverage | ComplianceTests, AdministrationAndPeriodTests, PrintableDocumentTests |

## SKILL 24 — DOCUMENTS, PRINTING, RECEIPTS & ATTACHMENTS

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Offline receipt print. | Native / hardware / deployed / CI acceptance pending; automated subset only | DocumentLibraryTests, PrintableDocumentTests, CaptureTests, SyncTests |
| Invoice PDF generation. | Native / hardware / deployed / CI acceptance pending; automated subset only | DocumentLibraryTests, PrintableDocumentTests, CaptureTests, SyncTests |
| Attachment sync. | Implementation / automated scenario evidence; review exact coverage | DocumentLibraryTests, PrintableDocumentTests, CaptureTests, SyncTests |
| Reprint after printer failure. | Native / hardware / deployed / CI acceptance pending; automated subset only | DocumentLibraryTests, PrintableDocumentTests, CaptureTests, SyncTests |
| Access-control test. | Implementation / automated scenario evidence; review exact coverage | DocumentLibraryTests, PrintableDocumentTests, CaptureTests, SyncTests |

## SKILL 25 — BACKGROUND WORKERS, RESILIENCE & OBSERVABILITY

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Retry then recovery. | Implementation / automated scenario evidence; review exact coverage | OperationsTests, OperationsScreenTests |
| Permanent failure dead-letter/diagnostic path. | Implementation / automated scenario evidence; review exact coverage | OperationsTests, OperationsScreenTests |
| Worker restart. | Implementation / automated scenario evidence; review exact coverage | OperationsTests, OperationsScreenTests |
| Health-check degradation. | Implementation / automated scenario evidence; review exact coverage | OperationsTests, OperationsScreenTests |

## SKILL 26 — DATABASE MIGRATIONS, VERSIONING & SAFE UPGRADES

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Upgrade from previous release. | Implementation / automated scenario evidence; review exact coverage | UpgradeTests, ProductionReadinessTests, SyncCompatibilityTests |
| Rollback/recovery rehearsal. | Implementation / automated scenario evidence; review exact coverage | UpgradeTests, ProductionReadinessTests, SyncCompatibilityTests |
| Old client against new server compatibility. | Implementation / automated scenario evidence; review exact coverage | UpgradeTests, ProductionReadinessTests, SyncCompatibilityTests |
| SQLite migration with existing unsynced outbox. | Implementation / automated scenario evidence; review exact coverage | UpgradeTests, ProductionReadinessTests, SyncCompatibilityTests |

## SKILL 27 — TESTING & QUALITY ENGINEERING

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| All critical suites pass in CI. | Native / hardware / deployed / CI acceptance pending; automated subset only | IntegrationTests project; scripts/package-windows.ps1 |
| No failing migration test. | Implementation / automated scenario evidence; review exact coverage | IntegrationTests project; scripts/package-windows.ps1 |
| No unbalanced journal scenario. | Implementation / automated scenario evidence; review exact coverage | IntegrationTests project; scripts/package-windows.ps1 |
| No duplicate sync transaction. | Implementation / automated scenario evidence; review exact coverage | IntegrationTests project; scripts/package-windows.ps1 |
| Import validation regression coverage. | Implementation / automated scenario evidence; review exact coverage | IntegrationTests project; scripts/package-windows.ps1 |

## SKILL 28 — DEPLOYMENT, SQL SERVER & AZURE/ON-PREMISE OPERATIONS

| Criterion | Current evidence boundary | Source index |
|---|---|---|
| Fresh install. | Native / hardware / deployed / CI acceptance pending; automated subset only | DesktopTests, RecordsSafetyTests, ProductionReadinessTests, SyncTests |
| Upgrade install. | Native / hardware / deployed / CI acceptance pending; automated subset only | DesktopTests, RecordsSafetyTests, ProductionReadinessTests, SyncTests |
| Backup/restore. | Implementation / automated scenario evidence; review exact coverage | DesktopTests, RecordsSafetyTests, ProductionReadinessTests, SyncTests |
| Offline branch during central outage. | Native / hardware / deployed / CI acceptance pending; automated subset only | DesktopTests, RecordsSafetyTests, ProductionReadinessTests, SyncTests |
| Azure and on-prem configuration validation. | Native / hardware / deployed / CI acceptance pending; automated subset only | DesktopTests, RecordsSafetyTests, ProductionReadinessTests, SyncTests |
