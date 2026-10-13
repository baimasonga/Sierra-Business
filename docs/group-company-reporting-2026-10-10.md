# Group company reporting

The Financial statements screen now includes **Group company reporting**. This source feature combines exported company/branch snapshots into profit-and-loss or balance-sheet reports, with a column for every member and a group total.

## Use

1. Sign in to a member company with business-read and accounting-read permission. Open Financial statements → Group company reporting.
2. Enter its display name and reporting dates, then export its company snapshot. Repeat for each member using the same dates.
3. Load two to twenty snapshots in the group-reporting screen. Review the member names, branches, periods and export timestamps.
4. Review account mappings. Equivalent accounts use the same group code and name; different meanings use separate codes. A group code cannot mix account types.
5. Build profit and loss or balance sheet. Export the report to Excel, including a Sources worksheet, or save the group and mappings as a JSON file for later restoration.

The Windows host saves generated files through its existing file-export service in Documents\Rokela Business Admin. The browser host prepares downloads. Changing a mapping, statement or member clears the displayed result until the report is rebuilt. Invalid file loads preserve the existing reporting set. Loading a valid saved group replaces the current reporting set.

## Controls and boundaries

- Export reads the configured signed-in branch under a serializable database transaction. It does not open another company's database or change posted records.
- Source identity derives from the immutable database history-chain origin and branch. Renaming or restoring a copy does not create a new member identity; duplicate sources are rejected.
- Loaded snapshots must use version 1 and NLe, matching reporting periods, unique account codes and balanced period/closing trial balances. Both balances use debit minus credit. File loads are limited to 5 MB and account counts to 10,000 per member.
- Profit and loss uses period income and expenses. Balance sheets use closing asset, liability and equity balances, plus accumulated profit/loss. Contributions remain visible even when member balances offset one another.
- Group totals are aggregates unless reviewed balanced elimination/adjustment entries are supplied. Their effect is shown in a separate column, saved with the reporting set, and exported in an Adjustments worksheet. Ownership/minority-interest calculations and currency translation are not automated. See the 0.32.0 notes for the separate company-period cutover feature.
- A member is an exported company/branch source; multi-branch companies need the relevant branches exported individually. The entered company name is a display label.
- Snapshot JSON is portable reporting data, not a signed certificate of origin. Review provenance and export timestamps; files may be edited or become stale. Saved group files contain financial balances and mappings. A new report does not fetch live member data automatically.
- Service calls recheck the current business/accounting-read permissions and branch. API routes are authenticated; the consolidation POST also uses the existing antiforgery check.

## Validation

The final targeted service and screen regression run passed **21 tests, zero failed and zero skipped**, in 2 minutes 12 seconds. It includes nine new consolidation tests and twelve existing smaller-gap tests, including financial statements and comparative-screen regression checks. The integration-project build succeeded.

Coverage includes contribution totals, reconciliation, mapping conflicts, malformed/unbalanced data, duplicate sources, period mismatches, permissions, posted-history export/reversal, Excel generation, screen result invalidation, saved-group restoration and native file-export selection. The result file is `tests/SierraBusiness.IntegrationTests/TestResults/consolidation-2026-10-10.trx`. The previous 355-pass full regression result predates this feature; this continuation ran the affected tests rather than rerunning the entire suite.

The API exposes authenticated `GET /api/v1/statements/company-snapshot?company=...&from=...&to=...` and `POST /api/v1/statements/consolidate`. The latter accepts a `ConsolidationRequest` and requires the existing antiforgery header. Service/screen behavior and compilation were verified; these new HTTP routes were not separately exercised over HTTP.

This is source implementation. No new Windows installer or installed-app acceptance is claimed.
