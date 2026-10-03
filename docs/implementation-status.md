# Implementation status and release gates

Latest full specification audit: [28-skill feature report](feature-audit.md) and [all 139 acceptance criteria](feature-acceptance-checklist.md). **The full system is not complete.** The native installer now exists and 28 .NET 10 tests pass, but no entire specification skill has full implementation and acceptance evidence. The audit records concrete control/UI gaps as well as missing modules.

The [0.1.1 bug-fix release](bugfix-release-0.1.1.md) addresses the audit's eight specific implementation findings and expands regression coverage to 40 passing tests. The original audit is a historical baseline; missing modules remain outstanding.

The supplied document is the product specification. Its full definition of done has **not** been met. This build implements a connected set of local trading workflows to establish the architecture and business invariants.

| Specification area | Current state | Still required |
|---|---|---|
| Architecture | Domain/Application/Infrastructure/API/Web/shared UI projects; native Windows MAUI and desktop services source added | Validate MAUI build; dedicated module registration as modules grow, Workers/Sync projects |
| Identity | Identity sign-in, owner bootstrap, app permissions, fixed branch | Role/user management, multi-company scoping, offline cached sessions, session revocation validation |
| Catalogue | Products, SKUs, base units, tracking modes | Barcode master, price levels, compatibility relationships, categories/unit masters |
| Inventory | Movement-derived stock, quantity/serial/batch, unit conversions, valuation | Multiple locations, transfers, reservations, counts/approvals, FEFO picker |
| POS/sales | Multi-line sales, payment details, credit settlement, guarded full reversal | Hold/recall, shift closing, partial returns, exchange, receipt printing, credit approval |
| Purchasing | Simple goods receipt and supplier payable | PO/GRN/invoice matching, FX, landed cost, supplier payments |
| Accounting | Balanced receipts/sales/reversals, trial balance, period date lock | Configurable chart/posting profiles, P&L/BS/cash flow, settlements, cheque lifecycle |
| Offline and sync | Local SQLite persistence and atomic pending operation log; MAUI local runtime source prepared | Native build/offline verification; inbox, upload/pull/bootstrap, cursor/version handshake, conflict queue, retry worker |
| Excel | Not implemented | Open XML templates, staging/mapping/validation, preview/commit, correction |
| Institutional | Not implemented | Quotations, tenders, approvals/versioning, orders/partial deliveries |
| Equipment/service | Serial identity in transactions only | Installation, warranty, service/repair/parts, field client |
| Reporting | Stock, documents, trial balance, audit | Server paging/filtering, report drill-down, export, branch comparison, SignalR |
| Compliance | Configurable development tax rate, preserved invoice rate | Effective dates, legal verification, fiscal adapter, full invoice fields |
| Production | Local setup and CI | SQL Server integration test, workload packaging, security/load review, tested upgrades and backup/restore |

## Deliberate restrictions

- Goods receipts are on supplier account only; the UI cannot imply a supplier was paid.
- A receipt with other activity for the same stock identity cannot be reversed; this avoids unsafe retroactive costing. Use a future valued purchase-return workflow.
- Full sale reversals restore original stock cost and reverse original journal lines. Partial returns and settled credit/cheque reconciliation are not supported.
- Stock and journal rows cannot be edited or deleted through application persistence. Database administrators remain technically powerful; production database permissions and independent audit retention are outstanding.
- Variant products use separate SKUs. Selecting compatibility mode does not create compatibility relationships.
- A branch deployment is a single configured company/branch database. It must not be shared across unrelated companies.
- Reports currently load branch history into memory. This is suitable only for development/small validation datasets until paging and reporting projections are implemented.
- Payment records are embedded in immutable document details in this slice. Full independently allocated payments and cheque state machines are a later migration.

## Next implementation order

1. Validate and harden the current slice with SQL Server, HTTP/authentication tests and browser workflow coverage.
2. Add location/customer/supplier masters and payment/cheque lifecycle; complete accounting reports.
3. Build MAUI local client using the same application service and shared UI, then test disconnected operation and restart.
4. Implement versioned server inbox, synchronization transport and multi-device conflict behavior; never sync balances as truth.
5. Add Open XML onboarding, orders, purchasing/FX/landed cost, tenders, equipment/service and production operations in the supplied roadmap order.

No manufacturing/BOM, mobile-money, card or cryptocurrency features are included.
