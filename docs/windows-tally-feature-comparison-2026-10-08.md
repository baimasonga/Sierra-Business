# Windows feature comparison — 8 October 2026

## Subsequent implementation

The next source increment adds six journal-based financial ratios with displayed formulas and Excel export; see [financial ratios](financial-ratios-2026-10-08.md). Liquidity classifications, comparative columns and the other advanced-accounting gaps remain open. The 0.30.0 installer does not contain that later increment. Navigation search also shipped in 0.29.1; this is screen navigation, not cross-company transaction search.

Version 0.30.0 adds a dedicated Finance > Deposits & advances workflow for customer deposits and supplier advances in NLe via cash/bank, with partial allocation, refunds and reversal dependencies. See [implementation and validation](trade-advances-0.30.0.md). This advances the first backlog item below; cheque advances, FX advances and broader acceptance remain outside that increment. The comparison tables retain their original review baseline.

## Scope and verdict

This compares the current 0.27.0-era source of Sierra Business / Rokela Business Admin with the supplied Sierra Leone specification and the relevant non-manufacturing capabilities documented by TallyPrime. **Android and manufacturing are excluded by the user's instruction and are not outstanding work.** Manufacturing BOMs, production orders and manufacturing job work are excluded; service jobs and commercial project costing remain relevant.

**Verdict: substantial business functionality is implemented, but complete non-manufacturing Tally parity is not established. There are real implementation gaps as well as separate acceptance gaps.** The original 0.1.0 audit is historical and substantially understates today's functionality. Conversely, a release note or a screen opening does not prove every workflow in that module is complete.

This is a source-and-documentation comparison, not a new runtime certification or an exhaustive comparison with every Tally add-on. Tally sources were checked on 8 October 2026, including the release-wise catalogue covering Release 7.0. Subscription, jurisdiction and bank-dependent Tally services are identified separately. No production records were changed and no external service was activated.

## How to read the findings

- **Implemented:** the stated workflow has application/service and UI evidence. This does not assert every edge case or full Tally-equivalent depth.
- **Partial:** a working subset exists, with the specific missing capability stated.
- **Not found:** no corresponding implementation was found in application contracts, services, persistence or shared UI searches. Manual journal workarounds are not counted as an automated workflow.
- **Acceptance pending:** source exists, but required real-world verification is incomplete. This is different from missing functionality.
- **Intentional adaptation:** the user's business requirement replaces a generic Tally convention.

The recorded browser verification on 5 October reports **287 passed, zero failed, six skipped out of 293 tests**. The later 0.29.0 installer run reports **295 passed, zero failed, six skipped out of 301 tests**; see [0.29.0 verification](sync-security-recovery-0.29.0.md). The six skips were two live SQL Server and four live PostgreSQL checks. Representative browser workflows and navigation through 46 screens were checked; that report expressly does not claim every action was exercised. These results were read, not rerun during this comparison. See [functional verification](ui-functional-verification-2026-10-05.md).

## Accounting and finance

| Capability | Current status | Evidence and remaining difference |
|---|---|---|
| Double-entry posting, journal/payment/receipt/contra, reversals | Implemented | [FinanceService](../src/SierraBusiness.Infrastructure/FinanceService.cs), [BusinessService](../src/SierraBusiness.Infrastructure/BusinessService.cs), FinanceWorkspace and FinanceTests. Operational transactions create journals; corrections preserve history. |
| Custom accounts and chart hierarchy | Partial | LedgerAccount supports custom accounts and five broad groups. No arbitrary nested account-group hierarchy comparable to Tally's configurable grouping was found. [FinanceEntities](../src/SierraBusiness.Infrastructure/FinanceEntities.cs). |
| Bill allocations, due dates, ageing, customer/supplier statements | Implemented | FinanceService, PartyService, Reports and PartyWorkspace. Overpayment and reversal checks exist. |
| Customer deposits, supplier advances and on-account settlement before invoicing | Not found as a dedicated workflow | FinanceService requires allocations to equal the trade control line and resolve to an existing bill. Return-generated customer/supplier credits and employee loans do not implement general trade advances. Tally supports unlinked/on-account references and later adjustment. [Tally tracking methods](https://help.tallysolutions.com/receivables-payables-tracking-methods/). |
| Interest on overdue invoices, loans and balances | Not found | No interest policy, calculation report or posting workflow found. Tally provides configurable interest calculations. [Tally interest](https://help.tallysolutions.com/interest-calculation-tally/). |
| Budgets and cost centres | Partial | Budget comparison and a CostCentre string on journal lines exist. Hierarchical cost/profit-centre masters, parallel cost categories and predefined percentage allocation classes were not found. [Contracts](../src/SierraBusiness.Application/FinanceContracts.cs), [Tally cost allocation](https://help.tallysolutions.com/cost-centre-or-profit-centre-tally/). |
| P&L, balance sheet, trial balance, day book, cash flow and funds flow | Implemented | [Statements](../src/SierraBusiness.Infrastructure/Statements.cs), Reports, StatementsWorkspace and report/export tests. Full Tally report customization is not implied. |
| Ratio analysis and side-by-side comparative statements | Partial / dedicated suite not found | Dashboard includes margin and trends. No dedicated current/quick ratio, turnover, return-on-investment suite or general multi-column comparative statements found. [ReportContracts](../src/SierraBusiness.Application/ReportContracts.cs), [Tally ratios](https://help.tallysolutions.com/ratio-analysis-tally/). |
| Optional/memorandum vouchers, reversing journals and financial scenarios | Not found | VoucherType contains Journal, Payment, Receipt and Contra. A normal reversal, held sale or approval request is not a scenario voucher. [Tally accounting entries](https://help.tallysolutions.com/accounting-entry-tally/). |
| Foreign-currency purchasing and realized exchange differences | Implemented | [Purchasing](../src/SierraBusiness.Infrastructure/Purchasing.cs) supports dated exchange rates, foreign bills and local settlement differences. |
| Foreign-currency sales, receipts, balances and period-end revaluation | Partial multi-currency coverage | BusinessService explicitly rejects foreign-currency sales: sales are invoiced in NLe. No general foreign-currency receivables, bank-ledger currency balances or unrealized revaluation workflow found. [Tally multi-currency](https://help.tallysolutions.com/multi-currency-accounting-made-simple-with-tallyprime/). |
| Period locks and financial year-end | Implemented | PeriodService and Statements.CloseYear; posted history remains available. Closing is not company-data splitting. |
| Multiple companies | Implemented | [CompanyDirectory](../src/SierraBusiness.Desktop/CompanyDirectory.cs) creates/selects separate company databases. |
| Group-company consolidated financial statements | Not found | Separate companies and branch dashboard figures exist; no member-company consolidation engine was found. [Tally group companies](https://help.tallysolutions.com/group-company-tally/). Intercompany elimination would be an additional design requirement, not presumed from simple aggregation. |
| Split company data by financial year/date | Not found | Backups and year-end records do not create verified before/after company datasets. [Tally split](https://help.tallysolutions.com/split-company-data-tally/). |

## Trading, stock and banking

| Capability | Current status | Evidence and remaining difference |
|---|---|---|
| Counter sales, barcode/pack entry, hold/recall and recoverable drafts | Implemented | PosService, DraftContracts, Workspace, PosTests and recorded browser hold/recall verification. |
| Bargaining with confidential minimum selling prices | Implemented; intentional adaptation | CommerceService/CommerceRules and server-side price checks. Preserve the owner's hidden floor; do not replace bargaining with mandatory fixed retail/wholesale tiers. |
| Promotions and quantity/customer/date pricing | Implemented subset | [Pricing](../src/SierraBusiness.Infrastructure/Pricing.cs) offers percent/fixed rules. Tally-style named price-level administration is not identical; the user's negotiated-price requirement governs. [Tally price lists](https://help.tallysolutions.com/selling-buying-prices/). |
| Quotations/proformas, sales and purchase orders, reservations, deliveries | Implemented | OrderService, CommerceService, OrderWorkspace and related tests. Delivery-linked invoicing has its own validation constraints; do not infer every possible delivery/invoice combination. |
| Goods received before invoice; supplier matching and variances | Implemented | [SupplierInvoices](../src/SierraBusiness.Infrastructure/SupplierInvoices.cs), SupplierInvoiceWorkspace and SupplierInvoiceTests. |
| Freight, duty and other landed costs | Implemented | Purchasing supports allocation by value, quantity or manual shares, including inventory/cost-of-sales treatment. |
| Sales/purchase returns, paid refunds and retained credits | Implemented | ReturnService, CommerceService, BankingService and tests. |
| Pre-invoice customer rejection / consignment-style stock documents | Partial | Returns focus on posted sale/receipt documents; no dedicated partial rejection-in against an uninvoiced delivery or general consignment stock workflow found. Reversing an entire delivery is not equivalent. [Tally inventory vouchers](https://help.tallysolutions.com/knowledgebase/inventory-entry-tally/). |
| Warehouses, transfers, physical counts and approval of count sessions | Implemented | InventoryService, InventoryManagement, CountSessionsWorkspace and inventory tests; includes serial/batch workflows. Older notes describing all of these as missing are superseded. |
| Batch/expiry, serial registers, FEFO, reorder and traceability | Implemented | InventoryManagement, inventory/commerce rules, TrackingWorkspace and tests. FEFO dispatch preference is distinct from FIFO accounting valuation. |
| Variants, unit conversions, bundles and compatibility | Implemented | Merchandise, InventoryManagement and Equipment use linked products, conversions and compatibility records. Do not mistake the older unsupported TrackingMode enum choices for today's separate merchandise/equipment workflows. |
| Stock valuation method selection | Partial | Movement-based carrying value and average-cost issuance exist. No configurable FIFO/LIFO/standard-cost/market-value selector found. [Tally valuation methods](https://help.tallysolutions.com/stock-valuation-methods-tallyprime/). |
| Stock ageing/slow-moving and general project profitability reporting | Partial | Batch first-received/expiry data, movements, sales and service cost records exist; no full dedicated stock-ageing/slow-moving suite or general commercial project-cost ledger found. [Tally inventory overview](https://help.tallysolutions.com/tally-prime/inventory/inventory-tally/). |
| Received and issued cheque tracking | Implemented | BankingService and BankingExtras provide distinct received/issued lifecycles and accounting links. |
| Cheque-book ranges, bank-specific cheque layout and cheque printing | Not found | BankingToolsWorkspace records written cheques; no cheque-dimension/layout/range/bulk-print workflow found. An invoice/payment-voucher printout is not a printed cheque. [Tally cheque handling](https://help.tallysolutions.com/banking-cheque-printing-tally/). |
| Statement import, bank formats and reviewed reconciliation | Implemented | BankingService, BankingExtras, statement format mapping and reconciliation history. |
| Connected bank balances/downloads/payments and voucher creation from bank rows | Not found | Imported statements and manual vouchers exist. No live bank connector/payment authorization workflow or dedicated bank-row-to-voucher creation found. Tally offers connected services for supported banks; availability is provider-specific, not automatically transferable to Sierra Leone. [Tally release catalogue](https://help.tallysolutions.com/tallyprime-features-release-wise/). |
| Cashier shifts, counted closing and assigned cash drawers | Implemented | BankingService, BankingExtras.Drawers/AssignDrawer, TillWorkspace. Physical drawer opening and printer integration still require acceptance evidence. |

## Payroll, documents, data and controls

| Capability | Current status | Evidence and remaining difference |
|---|---|---|
| Salaries, proration, entered overtime/bonus/deductions, payslips and payments | Implemented | PayrollService, PayrollWorkspace and payroll tests. |
| PAYE/NASSIT settings, schedules/remittances, employee loans and salary bank files | Implemented | [PayrollContracts](../src/SierraBusiness.Application/PayrollContracts.cs) and PayrollService. This confirms software capability, not current legal correctness or acceptance by a particular bank/authority. |
| Attendance register, leave balances and configurable pay-head formula engine | Partial | Monthly adjustments accept unpaid days and monetary overtime; no full attendance-voucher/register or arbitrary pay-head calculation engine found. Leave allowance is not leave entitlement/accrual tracking. [Tally payroll FAQ](https://help.tallysolutions.com/payroll-faq/). |
| Tax categories, dated rates, company identifiers and output-tax reporting | Implemented subset | Compliance and PeriodService retain posted tax and produce an output-tax reconciliation. |
| Purchase/input-tax recovery and complete statutory return workflow | Partial | Compliance.TaxReturn selects sales/service/returns and reconciles output tax. Supplier input-tax credits, full net-return preparation and authority submission are not established by that report. Exact Sierra Leone rules must be verified separately before implementation. |
| Live fiscal-device / tax-authority integration | Not implemented by the default connector | [Compliance.NoFiscalDevice](../src/SierraBusiness.Infrastructure/Compliance.cs) reports Available=false; DesktopRegistration selects it by default. Queueing/retries and an interface exist, but they are not a functioning authority connector. |
| Invoice/receipt/proforma/letterhead templates and report exports | Implemented | TradeDocumentPreview, document components, Compliance templates and Statements.Export. Physical output and some browser download delivery remain acceptance gaps. |
| Offline scanning/OCR, searchable library and optional online assistance | Implemented custom scope | Windows capture services, DocumentLibrary, previews and capture/library tests. Human review remains necessary; this is not blanket automatic posting of every scanned document. |
| Built-in email/WhatsApp document delivery | Not found | Export/printing does not provide an in-app delivery queue or delivery-status workflow. [Tally sharing](https://help.tallysolutions.com/tallyprime-with-whatsapp/share-documents-using-whatsapp-for-business/). No messages were sent during this audit. |
| Excel onboarding and imports | Partial | [ImportType](../src/SierraBusiness.Application/ImportContracts.cs) covers products, opening/serial/batch stock, price updates and stock counts. General party/ledger/voucher/complete-company migration and Tally XML/JSON compatibility were not found. [Tally import](https://help.tallysolutions.com/import-data-in-tally/). |
| APIs / external reporting access | Partial | Application HTTP endpoints and sync contracts exist. No Tally-compatible ODBC/TDL/XML interface or general user-facing company JSON import/export found. Internal JSON transport is not such an importer. [Tally data exchange](https://help.tallysolutions.com/import-data-from-xml-or-json/). |
| User permissions, approvals, exception review and immutable history | Implemented | UserAdministration, Controls, Integrity and provider history guards. Full role/device acceptance and security review remain separate. |
| Global cross-company search / command navigation | Partial | Screen filters, document search and shortcuts exist. No Tally SmartFind-style cross-company master/transaction search or complete command palette found. [Tally release catalogue](https://help.tallysolutions.com/tallyprime-features-release-wise/). |
| Offline branches, head office sync, SQL Server and PostgreSQL providers | Implemented, live acceptance pending | Sync, provider projects, SyncWorkspace and provider tests. The latest suite skipped six live-provider checks. This must no longer be described as an absent transport. |
| Automatic backups, restore and integrity checks | Implemented local scope | DesktopStore/BackupManager include automatic loop, retention, copy folder, restore and seals. No independent backup worker running while the desktop is closed or managed cloud backup service found. Tally's current catalogue includes those broader backup options. [Tally releases](https://help.tallysolutions.com/tallyprime-features-release-wise/). |
| Windows single-package delivery, upgrades and update notifications | Implemented, acceptance pending | MAUI/Inno package, pre-upgrade backup, rollback rehearsal and update-feed validation. Certificate/signing configuration, distribution feed, clean-machine and counter-hardware acceptance remain deployment work. |

## Original specification: current coverage by area

This is an area-level mapping, not a retrospective sign-off of all 139 old acceptance bullets.

| Specification areas | Current assessment |
|---|---|
| 1–3: architecture, persistence and offline sync | Shared .NET architecture and three database providers exist. Sync is implemented. Exact prescribed project/module boundaries, device/bootstrap compatibility and live-provider acceptance need a focused technical conformance check. |
| 4–8: shared UI, Windows client, web, API and identity | Shared/native/web interfaces and permission administration exist. The old Android requirement is explicitly removed. Keyboard/accessibility, all-role native acceptance and generated OpenAPI documentation remain unverified or not found in startup wiring. |
| 9–15: inventory, imports, POS, orders, purchasing, accounting and banking | Substantial implementation exists. The detailed tables above distinguish mature workflows from advances, FX breadth, advanced accounting and cheque-printing gaps. |
| 16–20: tenders, equipment, merchandise, CRM and service | Tenders/bid revisions/award handling, compatibility/equipment history, installation/warranty, variants/bundles, parties/credit and repair/maintenance services are implemented. Evidence: Tenders.cs, Equipment.cs, Merchandise.cs, PartyService.cs, Service.cs, their workspaces and corresponding test classes. Full end-to-end acceptance is still needed; these modules must not be marked wholly missing. |
| 21–24: reports, approvals, local compliance and documents | Implemented subsets are substantial; advanced reports, purchase-tax recovery, live fiscal connectivity and document-sharing differences remain as identified above. |
| 25–28: background jobs, migrations, quality and deployment | Operations jobs/reminders/diagnostics, provider migrations, upgrade tests and installer exist. Live provider, hardware, clean-machine, signing and operational recovery acceptance remain. |

The specification allows cash, cheque and bank transfer; credit is a sale condition. Card, mobile-money, UPI and crypto payment rails are therefore **not automatic backlog items**, even when offered by Tally or its partners. India/GCC/Kenya tax integrations are recorded as jurisdiction-specific differences, not silently added as Sierra Leone requirements. Fixed mandatory price tiers are not a replacement for the user's bargaining model. The later PostgreSQL provider is recorded as current implementation, not removed merely because the original technology document predates it.

## Recommended implementation backlog

The order below is based on day-to-day usefulness and dependency, not a claim that the user has requested each optional integration to be activated.

1. **Trade advances/deposits and later allocation.** Customer/supplier subledgers, partial application, refunds, reversals and statements before an invoice exists. Highest practical gap for institutional orders and deposits.
2. **Advanced accounting controls.** Interest calculations, nested account groups, richer cost-centre allocation and ratio/comparative reporting; keep optional charges configurable.
3. **Complete relevant FX workflows.** Foreign-currency sales/receipts and bank balances, realized/unrealized differences and controlled revaluation. Purchasing-only FX is already implemented.
4. **Payroll attendance and leave administration.** Attendance records, leave balances, hours-to-pay rules and flexible pay heads, retaining dated PAYE/NASSIT settings.
5. **Multi-company reporting and data management.** Consolidated statements, account mapping and verified company splitting; do not confuse branch totals with separate-company consolidation.
6. **Inventory/accounting depth.** Selectable valuation methods, stock ageing/slow-moving analysis and pre-invoice rejection/consignment workflows. Changing valuation needs historical and migration safeguards.
7. **Banking/document productivity.** Cheque-book/layout printing, reviewed voucher creation from statement rows, broader imports and optional email/WhatsApp sharing.
8. **Provider-dependent integrations and release acceptance.** Actual bank/fiscal connectors, verified statutory scope, closed-app/offsite backup arrangements, live-provider checks, physical hardware and signed clean-machine release.

Optional/memorandum/scenario vouchers, global search and reporting/export compatibility remain comparison gaps and can be scheduled alongside those groups. No percentage-complete figure is assigned: rows differ greatly in complexity and applicability.

## Evidence limitations and next validation

- This review inspected contracts, core service guards, schema/entity shapes, UI inventory, tests and existing execution records. It did not rerun 293 tests or install over the live company.
- Findings labelled Not found are bounded by that source search, not proof that every third-party extension or future branch was inspected.
- [Acceptance checklist](acceptance-checklists.md) remains open for counter hardware, clean Windows installation/upgrade/uninstall and two-machine offline/reconnect testing.
- Do not equate passing navigation or a generated installer with business/legal certification.
- Reconcile the historical 139-item checklist in detail as each backlog group is accepted; retain the historical audit rather than rewriting its old results as current results.
