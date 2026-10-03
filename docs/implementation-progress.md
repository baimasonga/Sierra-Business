# Non-manufacturing implementation progress

User instruction: implement Tally's business features except manufacturing, preserving .NET 10/MAUI and a single Windows installer. Full parity is NOT complete. Source implementation, automated validation and acceptance on real hardware are different milestones.

## Implemented after 0.2.0

- Accounting: custom ledger definitions; journal, receipt, payment and contra vouchers; protected trading controls; bill allocations with overpayment/date checks; reversal interlocks; historical ageing; P&L, balance sheet, period trial balance and day book; cost centres; budgets versus actual; cash/bank statement matches.
- Quotations, sales orders and purchase orders: immutable terms, quotation conversion, closure, partial invoice/receipt fulfillment, overfulfillment and price checks, reversal-aware quantities.
- Warehouses: masters, location-specific balances and transactions, transfers retaining serial/batch identity and value, physical counts for quantity-controlled items with stale-count protection.
- Parties: customer/supplier/contact masters, version checks, payment terms, customer credit limits and holds. Legacy unregistered party names remain supported and do not receive master credit controls.
- Returns: partial sales credit notes and purchase debit notes; original-price/tax credit, stock valuation, rounding reconciliation, bill settlement limits and reversal interlocks. Returns do not reopen order fulfillment; paid sales support cash/bank refunds or held customer credits with partial refund/allocation workflows (0.4.0).
- Payroll: employees, full-month fixed salary/allowances/entered deductions, immutable employee snapshots, duplicate monthly run prevention, register and payslips. Individual employee payment allocation and statutory calculations remain absent.
- Users: account creation, permission presets and individual permissions, lock/unlock, password reset, security-stamp invalidation, self-lockout protection and audit records without passwords.
- Period controls: irreversible period closing, effective-dated future sales tax rates and transaction-level closed-period checks. No jurisdiction-specific filing/certification or supplier input-tax workflow.
- Companies: native selection and separate company databases/accounts, preserving the original data directory. Company switching needs native acceptance testing.
- Printing: sales invoice/goods receipt previews with posted amounts and reversal marking, order/quotation print views and payslips. Print dialogs allow a configured PDF printer. Real native print output remains unverified; no statutory invoice certification is claimed.
- Authenticated APIs and shared desktop/web UI, antiforgery protection for mutations.

## Verification on 2026-09-28

77 integration tests passed, including migration from the actual 0.2 schema, old-operation replay compatibility, period locks, user security and printable invoice escaping/historical prices. HTTP checks passed for sign-in, trading/reversal/idempotency, module reads, vouchers, bank matches, warehouse transfer, user creation, print helper delivery and antiforgery rejection. EF reports no pending model changes. The Windows packaging run completed successfully: artifacts/windows/0.3.0/SierraBusiness-Setup-x64.exe. Its gate repeated all 77 tests, passed Windows OCR, published MAUI and compiled Inno Setup. The independently checked checksum matches SHA256SUMS.txt.

## Additional source work for 0.3.1

Cash-flow movement reporting now reconciles opening cash/bank plus net receipts less payments to closing balances, excluding internal transfers. Bank statement matches can be cancelled with an immutable reason/history and then replaced, without altering journal balances. Financial statements and cash flow now have print controls. 80 integration tests passed; extended HTTP checks passed cancellation and cash-flow reconciliation. See accounting-improvements-0.3.1.md for semantics and limits.

## Negotiated sales, delivery and credits — 0.4.0

The user clarified that negotiated prices replace fixed retail/wholesale tiers. A separate confidential minimum is now required and enforced server-side for quotations, sales orders, deliveries and invoices, including converted units. Only pricing.manage users can view/change it; it is absent from ordinary product responses and customer documents. No minimum is guessed for old/new products: configure it before quoting/selling. Owner upgrades receive the new permission; ordinary clerks do not.

Sales orders now support reservations by warehouse/tracking identity and partial delivery notes. Deliveries issue stock into a delivered-goods clearing account; invoices consume the delivery link without issuing stock again. Each delivery is invoiced in full, with separate notes for staged invoices. Reversal ordering and reservations protect inventory. Paid sales returns can split unpaid bill credit from cash/bank refund or held customer credit. Credits can be partly refunded or applied to the same customer's outstanding invoice, with overuse checks and audit history. Supplier overpayment returns remain outside this change.

Proforma views, delivery notes without prices, settled-sales receipts, sales credit notes, purchase debit notes and customer-refund/allocation printouts are included. See negotiated-sales-0.4.0.md for usage, accounting semantics and remaining acceptance work. The package log records final validation; no full Tally parity is claimed.
## Banking and cash control — 0.5.0

CSV bank statements now have bounded preview/import, duplicate-content/identifier checks, balance validation, suggested exact-amount matches, explicit review, cancellation/replacement and immutable void records. Received cheques support automatic registration from new sales, receipt against customer bills, deposit, clearance, bounce debt, settlement and audited undo with stale-version/reversal interlocks. Till shifts capture cash-ledger postings, opening/book differences, staff attribution, stale closing protection and immutable counted shortages/overages. Paid purchase returns split unpaid bill credit from actual refunds or held supplier credits, with partial refund/allocation and overuse protection.

See [banking and till instructions](banking-cash-control-0.5.0.md) for accounting semantics and limits. Shared tills follow a ledger, not a per-salesperson drawer. Variances are recorded for investigation rather than automatically expensed. Bank formats require Date,Reference,Amount CSV; no bank transfer is initiated. Legacy cheques require review of earlier manual postings before registration. Full Tally parity remains unfinished.
## Counter sales — 0.6.0

New sale now supports persistent local held drafts, recall/replacement/cancellation with audit history, and atomic completion with the posted sale. Failed current-price, stock or tracking validation preserves the hold. Concurrent/stale recalled versions cannot post twice. Drafts do not reserve stock or affect money. Recall refreshes stock and uses today's business date.

Barcode and SKU keyboard-scanner input supports base-unit/pack conversions and leading zeroes; tracked goods are selected for mandatory tracking entry. Product filtering, keyboard focus/hold shortcuts and editable agreed line prices speed counter work while retaining confidential minimum enforcement. Existing receipts and cash-ledger till capture apply to posted sales. See [counter-sales instructions](counter-sales-0.6.0.md) for workflow and limitations.
## Batch stock counts — 0.7.0

Physical counts now support exact batch/expiry identities per warehouse, including newly discovered and expired lots. Stale quantities and reservation undercounts are rejected; shortages retain lot valuation and surpluses require explicit cost. See [batch-count instructions](batch-counts-0.7.0.md). Serial counts and FEFO remain outstanding.

## Earliest-expiry counter sales — 0.8.0

Counter sales can add suggested batch lines in earliest-expiry order, excluding expired and reserved stock and subtracting existing draft allocations. Suggestions use current warehouse stock, retain agreed base-unit prices and show expiry dates for review. Final posting revalidates stock. See [workflow and limits](earliest-expiry-0.8.0.md).

## Serial counts — 0.9.0

Physical counts now support single serials with present/absent checks, valued shortages, new-serial surpluses, global history uniqueness, reservations, replay and reversal protection. See [instructions](serial-counts-0.9.0.md). Bulk serial count sheets remain outstanding.

## Remaining implementation and acceptance

Advanced promotion/discount rules beyond negotiated prices, bank-specific import mappings, issued-cheque workflows, classified cash-flow and funds-flow reports, complete report exports and document customization, payroll attendance/proration/statutory formulas/individual payment allocation, fiscal reporting/integrations, multi-currency/landed costs, approvals, multi-user synchronization and SQL Server migrations, company consolidation/year-end/split/restore UI, Excel onboarding, inventory variants/barcode labels and reassignment/bulk serial count sheets/order-specific FEFO dispatch, separate POS drawer assignment, complete tenders/service/equipment workflows, Android, keyboard/accessibility/large-data validation, native company/print acceptance, release signing and clean-machine/hardware verification.

Reference scope: [TallyPrime help](https://help.tallysolutions.com/tally-prime/) and [feature categories](https://tallysolutions.com/global/tally-prime/). No proprietary Tally source/assets are included. Bank and tax-authority connections require provider-specific implementations.





Release 0.4.0 is packaged and checksum-verified, with 91 passing integration tests, extended HTTP workflow/security checks and offline OCR/native publishing checks. Hardware/native UI acceptance remains outstanding.


Release 0.5.0 is packaged at artifacts/windows/0.5.0/SierraBusiness-Setup-x64.exe. All 101 integration tests, expanded HTTP checks, offline OCR, native publish, installer compilation and independent checksum verification passed.


Release 0.6.0 packaging is complete: 108 tests, offline OCR, native publish and installer compilation passed. Installer version and checksum independently verified on 2026-09-29. See verification.md for evidence and remaining acceptance limits.

Release 0.7.0 is packaged and independently checksum-verified. All 113 tests, extended authenticated HTTP checks, offline OCR, native publish and installer compilation passed. See verification.md for evidence and remaining acceptance limits.

Release 0.8.0 is packaged and independently checksum-verified. All 119 tests, extended authenticated HTTP checks, offline OCR, native publish and installer compilation passed. See verification.md for evidence and remaining acceptance limits.

Release 0.9.0 is packaged and independently checksum-verified. All 125 tests, authenticated HTTP checks, offline OCR, native publish and installer compilation passed. See verification.md for evidence and remaining acceptance limits.

Navigation redesign 0.10.0 replaces the flat menu with seven permission-filtered business areas and contextual screen navigation. Browser interactions and responsive layout verified; see navigation-0.10.0.md.

Release 0.10.0 packaged and checksum-verified: 126 tests, authenticated HTTP checks, responsive browser navigation, offline OCR and native publishing passed.

## Records safety — 0.10.2

Source baseline 0.10.1 (2026-10-03) fixed a stale barcode-scan test and added 13 inventory-management tests (139 passing). Release 0.10.2 adds database guards and a verifiable seal chain over posted history, automatic and off-machine backups, an owner-level restore with safety backup, and separation of duties for stock-count approval. 157 tests pass. See [records-safety notes](records-safety-0.10.2.md). Next per the specification roadmap: Excel onboarding (Skill 10, Phase 2 exit condition), then foreign-currency purchasing, landed cost and supplier-invoice matching (Skill 13, Phase 4).

## Excel import — 0.11.0

Specification Skill 10 implemented: six generated templates, Open XML parsing with safety limits, column mapping, row-level validation using the manual-entry rules, staging (ImportBatch/ImportRow), error workbooks, retry-safe commits with in-transaction re-validation, and import history. Opening stock posts per-warehouse documents against the new 3200 Opening balance equity account. This meets the roadmap Phase 2 exit condition. 174 tests pass. See [Excel import notes](excel-import-0.11.0.md). Next: foreign-currency purchasing, landed cost and supplier-invoice matching (Skill 13, Phase 4).

## Purchasing, imports and landed cost — 0.12.0

Specification Skill 13 implemented for roadmap Phase 4: effective-dated exchange rates, foreign-currency goods receipts carried in NLe with foreign bills, foreign supplier payments with realised exchange gains/losses (4200/5300), and landed-cost documents allocated by value, quantity or manual amounts, split between stock on hand and cost of sales, payable or paid. Purchase returns of foreign receipts use the receipt's rate. 185 tests pass. See [purchasing notes](purchasing-imports-0.12.0.md). Remaining in Skill 13: separate supplier-invoice matching (GRNI/price variance) and foreign-currency purchase orders. Next per roadmap: Phase 6 institutional/equipment workflows or Phase 5 synchronization.

## Supplier invoices and foreign-currency purchase orders — 0.13.0

Completes Specification Skill 13 (Purchase Order → Goods Receipt → Supplier Invoice → Supplier Payment): receipts may wait for the supplier's invoice in 2050 Goods received not invoiced; supplier invoices are matched to receipt lines with partial quantities, price differences split between stock on hand and cost of sales, and exchange differences between receipt and invoice dates; purchase orders can be in a foreign currency; returns of uninvoiced goods reduce 2050. Credit sales now require a registered customer so credit limits and holds always apply. 196 tests pass. See [supplier invoice notes](supplier-invoices-0.13.0.md). Phase 4 is complete. Next per roadmap: Phase 6 institutional and equipment workflows (tenders, installation, warranty, service), then Phase 5 synchronization.

## Equipment lifecycle, installation and warranty — 0.14.0

Phase 6, first release: Specification Skill 17 and the installation/warranty parts of Skill 20. Versioned per-product warranty and installation terms with commissioning checklists; product compatibility links (accessory, consumable, spare part); universal serial lookup with status, customer, supplier warranty, unit specifications and full document timeline; installation scheduling, checklist-enforced completion with customer acceptance and printable certificate; customer warranties started at sale or installation, void on reversal/return and renewed on resale. Printed documents now show the real company name and branch. 203 tests pass. See [equipment notes](equipment-installation-warranty-0.14.0.md). Next: 0.15.0 service jobs, parts, warranty claims and preventive maintenance; 0.16.0 tenders.

## Document library — 0.15.0

Prioritised ahead of the remaining Phase 6 work at the owner's request (Specification Skill 24, documents and attachments). A shared document library replaces the desktop-only capture inbox: files, scanner pages and phone photos; on-device text reading with suggested details; types for tenders, cheques, invoices, contracts, delivery notes, certificates and correspondence; fingerprinted, content-addressed originals with versions; details history; void with reason; confidentiality; links and attachment panels for transactions, parties, cheques, serial units and installations; deadlines and expiries. Earlier captures are migrated. The database plan (SQL Server support, SQL Server Express replica, Azure SQL with Blob Storage for multi-site) is documented. 211 tests pass. See [document library notes](document-library-0.15.0.md). Next: 0.16.0 service jobs or tenders, then SQL Server support.

## Tenders and document previews — 0.16.0

Specification Skill 16 implemented: tender register with closing countdowns, bid costing (supplier cost, exchange rate, landed cost, margin, alternatives, tax), immutable bid versions, price approval with separation of duties, locked submissions with clarification/resubmission, closing-date extensions, won tenders converted to sales orders at bid prices, lost/cancelled closure with feedback, tender document links and printable price schedules. Document library redesigned as a card gallery with first-page pictures and in-place previews (PDF via bundled pdf.js, TIFF via UTIF.js, photos, and formatted Word/Excel/PowerPoint/CSV renderings made on the computer). 219 tests pass. See [tender and preview notes](tenders-document-previews-0.16.0.md). Next: service jobs, parts, warranty claims and preventive maintenance (completing Phase 6).

## Service jobs and preventive maintenance — 0.17.0

Specification Skill 20 completed (Phase 6): service jobs with priority, engineer assignment, diagnosis, labour and customer acceptance; parts issued from a chosen warehouse as ordinary stock documents at average cost into service work in progress (1260), returned unused at their issue cost, and never moved again on closing; explicit billing decisions (chargeable, warranty claim with parts/labour scope, maintenance contract, goodwill) with warranty validated against the sold serial and exceptions requiring pricing authority; closing posts a service invoice (service income 4300, tax, cost of sales) collectible like a credit sale, or writes covered parts off to warranty and service costs (5400); supplier warranty claims; preventive maintenance plans generating planned, checklist-driven visits once each; service history on the serial timeline; printable service report and invoice; evidence attachments. 226 tests pass. See [service notes](service-maintenance-0.17.0.md). Next: SQL Server support (on hold at the owner's request) or reporting dashboards (Skill 21).

## Dashboard and drill-down reports — 0.18.0

Phase 7, first release: Specification Skill 21. Owner dashboard (net sales, gross profit and margin, cash and bank, receivables and payables with overdue amounts, stock value and reorder alerts, service, tender and warranty counts, daily or monthly sales trend, top customers and products, reconciliation checks, branch comparison) refreshing live through an in-process change feed with a two-minute poll fallback; drill-down from every figure through sales analysis (day, month, customer, product), paged day book and ledger registers, stock valuation and paged stock movements, and receivables/payables ageing, to the source document. All figures come from the journal or stock movements and carry reconciliation checks. 232 tests pass. See [dashboard and report notes](dashboard-reports-0.18.0.md). Next: audit and fraud controls with approval workflows (Skill 22).
