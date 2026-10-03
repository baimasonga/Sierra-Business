# Bug-fix release 0.1.1

This release addresses the eight source-level findings in the feature audit. It does not implement the missing business modules or certify that no undiscovered defects remain.

| Audit finding | Change |
|---|---|
| Desktop settings not bound | Desktop reads validated `trading-settings.json` from its Data directory. Branch/device, tax and closed-through date are applied to the shared service. Startup rejects a branch inconsistent with existing accounts/documents. |
| Stale web permissions | Every web business operation reloads current Identity permissions, branch, lockout and security stamp. A changed stamp, deleted/locked account, changed scope or expired session rejects the operation. Existing web sessions must sign in again. |
| Navigation discards drafts | Re-entering the current screen preserves the draft and operation ID. Leaving an unfinished trade or signing out asks for confirmation; Cancel keeps the draft. Navigation is blocked while posting. |
| Stale unit price | Product selection and accepted lines reset price/tracking inputs. Receipts require explicit cost; sales fallback multiplies base-unit catalogue price by unit conversion. New trade screens reset settlement/reference/date state. |
| Sales KPI ignores reversals | Net sales now come from Sales ledger credits minus debits, excluding tax and including reversal effects. The overview labels this explicitly. |
| Persistence history bypass | Both synchronous and asynchronous save paths, including boolean overloads, reject modifications/deletions of documents, movements, journals and audit operations. No general acknowledgement-edit exception exists until a controlled sync mechanism is implemented. |
| Unsupported tracking options | Variant/Compatibility are no longer offered for new products and the application service rejects them. Existing records are preserved; separate quantity SKUs remain the supported workaround. |
| Empty health check | Database readiness checks connectivity, pending migrations and the product schema. Failures return unhealthy without exposing exception details. |

Additional fixes: null trade lines return business validation errors; invalid antiforgery tokens return a structured HTTP 400 response. Tests cover all three payment methods, reversals, configured tax retention, session validation, draft cancel/confirm behavior, line-price resets, audit protection and missing-schema health failure.

## Validation

All 40 .NET 10 regression tests pass. The solution builds without warnings/errors, and EF Core reports no pending model changes; no schema migration is needed. The repeatable `scripts/verify-web.ps1` exercises a separate temporary database with generated test credentials, never the desktop business database. The installer is produced only after the packaging script reruns integration tests and native publish successfully.

## Desktop configuration

The file is `%LOCALAPPDATA%\SierraBusiness\Data\trading-settings.json`. It is created with the previous defaults for compatibility. Close the application before editing; restart to apply changes. Set branch/device before creating the owner or posting records. Do not change branch on an existing database; startup now rejects this. Tax rates are fractions (for example, 0.10 is a synthetic 10% example, not statutory guidance). `ClosedThrough` uses `yyyy-MM-dd`. Invalid settings stop initialization instead of silently reverting to defaults.

There is no settings administration screen yet. This is a configuration-file fix, not completion of the configuration/compliance module. Protect this file using the Windows account and filesystem access controls. Back up the settings file alongside database backups; restore the matching branch configuration with a database from another machine. Tax changes apply only to future posts; posted invoice details retain their original rate.

## Remaining limits

Draft confirmation protects in-app navigation and sign-out; crash recovery and persistent hold/recall are still missing. Account administration and central sync are still missing. App-layer immutability does not protect against direct SQL by a database administrator. The dependency health check does not prove disk-write capacity or all storage dependencies. Automated component-handler tests are not a full visual/native UI test. See the feature audit for outstanding modules and release acceptance work.
