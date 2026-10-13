# Local feature completion — 0.31.0

External bank, fiscal/tax, email and WhatsApp connectors are deferred at the user's request on 9 October 2026. This release closes local reporting and productivity gaps in the original specification. The broader Tally comparison remains a separate expansion backlog; this document does not declare those additional capabilities complete.

## Implemented in this build

- Comparative profit/loss, balance sheet, cash flow and funds flow, with two independently selected periods, aligned account rows, absolute/percentage changes and Excel export. A zero prior value has no percentage change.
- Stock ageing and slow-moving stock by warehouse and tracking identity. Receipt layers retain their age through transfers; reversals are evaluated as of the report date. Returns and positive count adjustments start a new layer at their document date. Unknown history is disclosed separately. Age allocation does not change weighted-average accounting valuation.
- Encrypted Windows recovery for quotation/order creation and tender headers or edited bids. Restoring a bid preserves its expected version and refuses to overwrite a newer revision. Partial order fulfilment is not recovered automatically.
- Saved per-user report dates, view, grouping, ledger and document-kind filters; configurable sale, receipt and screen-search function keys. These preferences contain no party names, amounts, credentials or draft documents. Existing Alt shortcuts remain. Ctrl+S uses the invoice's normal Post document action.
- Authenticated OpenAPI documentation and branch-scoped SignalR invalidation. Notifications carry no business records and are sent after transaction commit; rolled-back writes produce no notification. Reconnecting clients must fetch current data.

## Current acceptance boundaries

The historical `feature-acceptance-checklist.md` remains a dated audit and is not the current list of missing modules. Existing inventory, sales, purchasing, accounting, banking, payroll, tender, equipment, service, approvals, capture, backup and sync implementations have corresponding integration tests. Passing those tests does not establish every physical-device or deployment criterion.

| Specification area | Current source/test evidence | Remaining acceptance |
|---|---|---|
| 1–2 Architecture and persistence | Shared projects/contracts; versioned SQLite migrations; provider projects and history guards | Execute SQL Server/PostgreSQL checks with live test connections |
| 3 Offline sync | SyncTests, SyncCompatibilityTests, SyncScreenTests; version negotiation and idempotent transport | Two installed devices, real outages and power interruption |
| 4–8 Clients, UI, API and permissions | SharedUI, WorkspaceHeaderTests, NavigationTests, API HTTP smoke; generated OpenAPI and authenticated notification reconnect | Native keyboard-only/accessibility/DPI and large-data performance acceptance; Android remains excluded |
| 9–15 Inventory and trading/finance | Inventory, Import, Pos, Order, Purchasing, Trading, Finance, Banking and return tests | Physical scanner/printer/drawer and representative customer data acceptance |
| 16–20 Tenders, equipment, merchandise, CRM, service | Tender, Equipment, CustomerMerchandise, ReturnAndParty, Service and screen tests | Installed end-to-end workflows with users and hardware |
| 21–24 Reports, controls, compliance and documents | Report, Control, Compliance, DocumentLibrary, PrintableDocument and Capture tests | Statutory correctness and fiscal device acceptance; external connectors deferred |
| 25–28 Jobs, upgrades, quality and delivery | Operations, Upgrade, Desktop, RecordsSafety tests; mandatory installer tests and offline OCR smoke | Clean-machine install/upgrade/rollback, deployed HTTPS sync, signing certificate and distribution feed |

Other gaps from the optional Tally comparison still include full sales/banking FX and revaluation, attendance/leave and configurable pay heads, interest/cost-centre/scenario accounting, company consolidation/splitting, selectable valuation/consignment, cheque-layout printing and broader migration imports. They require further implementation and must not be represented as delivered by this release.

## Verified release — 9 October 2026

- Full release suite: 318 passed, zero failed, six live database-provider checks skipped (324 total).
- Web Release build: zero warnings/errors. Authenticated HTTP checks passed, including generated API documentation, comparative reports, stock ageing, notification authorization/reconnect, transaction replay and reversal.
- Offline capture/OCR smoke passed with a 94% OCR score. JavaScript syntax validation passed.
- Native Windows publish and Inno Setup compilation succeeded. Installer: `artifacts/windows/0.31.0/RokelaBusinessAdmin-Setup-x64.exe`, version 0.31.0, 290,028,351 bytes.
- SHA256: `E7AAD2374CA3E158F10E36F864ECE14D6BCE34F7132EF3B7542A4ACBBEAE8249`.
- Installer remains unsigned. No installation over the user's live company or native/hardware sign-off was performed.
- The verified package used the immutable 423-file `artifacts/windows/source-0.31.0` source snapshot and its hash manifest. TRX and build logs are retained in the release folder. The separately updated 139-criterion evidence index is `current-feature-evidence-2026-10-09.md`.
- Subsequent release audit: 26 artifact checks passed using `scripts/verify-windows-release.ps1`. Native acceptance remains pending because the Windows computer-use runtime could not initialize. See `windows-acceptance-0.31.0.md` for the exact boundary and executable next-session checklist.
