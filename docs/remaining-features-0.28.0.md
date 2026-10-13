# Remaining implementation review — Rokela Business Admin 0.28.0

Reviewed 7 October 2026 against engineering-specification.md, current source,
release notes and acceptance evidence. This is a source/document review, not a
new execution of all 139 acceptance criteria. Latest installer verification:
289 tests passed, zero failed, six live-provider tests skipped.

Follow-up: 0.29.0 adds sync protocol/schema negotiation, DPAPI-protected desktop
sync keys and encrypted sale-draft recovery. See sync-security-recovery-0.29.0.md
for coverage and limits. The table below is retained as the 0.28.0 review baseline;
recovery for other document types and native/deployment acceptance remain open.

## Implemented core

Inventory, stock counts/transfers/reservations/FEFO, barcode masters, variants and
bundles, sales and held drafts, returns, purchasing and landed cost, accounting,
banking, payroll, customers/suppliers, tenders, equipment/service, document
capture, reports, approvals, background jobs and branch sync have implementations
and automated coverage. This does not sign off every specification requirement.
The original feature-audit.md and implementation-status.md describe the initial
release and should not be used as current missing-module lists.

## Confirmed remaining work

| Priority | Area | Remaining implementation or acceptance |
|---|---|---|
| 1 | Windows acceptance | Verify the installed 0.28.0 app on clean Windows, native form clicks, DPI/multiple monitors, offline restart, upgrade/rollback/uninstall and interrupted posting. Browser/component checks do not establish these results. |
| 1 | Sync compatibility | Add explicit client/server protocol and schema negotiation, a supported-version matrix, clear incompatible-client rejection and old/new client tests. SyncContracts currently has no protocol-version handshake; row Version fields are concurrency fields. |
| 1 | Deployment security | Encrypt desktop sync secrets at rest; DesktopSyncConfiguration currently writes the key in JSON. Enable signing using an actual certificate, verify the signed release, and configure/publish the update feed. |
| 2 | Hardware integration | Browser print layouts and keyboard barcode input exist. Dedicated printer/cash-drawer behavior and real scanner/thermal/A4/device acceptance remain. NoFiscalDevice is the default; fiscal queue/retry exists but a real device/service connector is still needed. |
| 2 | Shared UI | Universal Go To/search command surface, configurable shortcuts, saved per-user grid/filter preferences, and broader reusable field/grid primitives remain. Verify keyboard-only and screen-reader workflows across roles. |
| 2 | Draft recovery | Explicit held sales are persisted. Ordinary unsaved invoice lines are in Workspace component memory; automatic crash recovery for unfinished long forms still needs implementation and verification. |
| 2 | API and notifications | Business action endpoints and ProblemDetails exist. OpenAPI generation/examples and a specification coverage check for integration-critical endpoints remain. Dashboard refresh uses local change notifications and polling; explicit business SignalR push is not implemented. |
| 3 | Android/field client | MAUI project targets Windows only. Android target, platform services, touch/field workflows, camera/device integration and offline device acceptance remain. |
| 3 | Live database and network acceptance | Re-run the two SQL Server and four PostgreSQL tests with configured test connections. Rehearse two installed branches against deployed HTTPS head office, outages/reconnect, conflict review and backups. Existing simulated/HTTP tests are useful but do not establish that deployment result. |
| 3 | Documentation and UI consistency | Update the old 28-skill/139-criterion matrix with current evidence. Catalogue help says compatibility relationships are unavailable, although Equipment supports them; direct Variant/Compatibility tracking modes are deliberately rejected while variant groups use distinct SKUs. Clarify that distinction. |

## Evidence

- Windows target and branding: src/SierraBusiness.Maui/SierraBusiness.Maui.csproj.
- In-memory invoice draft and stale help: src/SierraBusiness.SharedUI/Workspace.razor.
- Sync wire contracts: src/SierraBusiness.Application/SyncContracts.cs.
- Plain JSON sync configuration: src/SierraBusiness.Desktop/DesktopSyncConfiguration.cs.
- Default fiscal connector: src/SierraBusiness.Infrastructure/Compliance.cs.
- Equipment relationships: src/SierraBusiness.Infrastructure/Equipment.cs.
- Branch comparisons and refresh: src/SierraBusiness.SharedUI/ReportsWorkspace.razor.
- API actions: src/SierraBusiness.Api/BusinessEndpoints.cs.
- Required UI capabilities: engineering-specification.md, Skills 4–7 and 26.
- Native/hardware/deployment acceptance: acceptance-checklists.md and
  ui-functional-verification-2026-10-05.md.
- Latest package evidence: rokela-branding-0.28.0.md.

Recommended next implementation: sync-version negotiation and protected desktop
secrets, followed by draft recovery and the shared navigation/preferences work.
Run native Windows acceptance alongside that work before customer distribution.
No honest completion percentage can be assigned until the full criterion matrix
is refreshed and each requirement has current evidence.
