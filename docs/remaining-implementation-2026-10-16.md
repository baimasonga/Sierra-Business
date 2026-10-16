# Remaining implementation — 16 October 2026

Authorization: implement all remaining features listed in [the 11 October backlog](remaining-backlog-implementation-2026-10-11.md). Bank, fiscal, email and WhatsApp connectors stay deferred under the earlier instruction. Android and manufacturing stay excluded.

This is source work after the immutable 0.32.0 release. The 0.32.0 installer does not contain it, and no new installer was built: native Windows packaging needs a Windows machine.

## Implemented in source

### Group reports (backlog item 3)

- **Non-controlling interests.** Each member company can carry the parent's ownership percentage. For partly owned members, the report splits profit (profit and loss) or equity (balance sheet) between owners of the parent and non-controlling interests. Group adjustments belong to the parent. Eliminate the parent's investment against its share of the member's equity with a reviewed group adjustment.
- **Currency translation.** A member snapshot may report in another three-letter currency. It needs reviewed NLe rates, which the screen leaves empty for foreign members rather than assuming 1:1:
  - assets and liabilities use the closing rate;
  - income and expenses use the average rate;
  - equity uses the historical rate.

  The balancing amount appears as a foreign currency translation reserve in equity. The terms are saved with the reporting set and appear in the Excel Sources sheet.
- **Matching across currencies.** Reciprocal intercompany account review compares translated balances.
- **Transaction-level matching.** Each company exports its documents with a named counterparty, signed by their effect on trade receivables and payables. Loading two exports does the following:
  - matches equal and opposite documents within 0–31 days;
  - lists unmatched documents on each side;
  - shows the remaining difference.

  Cash-settled trades that never touch receivables or payables are not included; review those at account level.

### Interest charges (item 5)

- The interest worksheet can now post a reviewed charge for a customer bill. Posting recalculates the worksheet, and the charge cannot exceed it.
- The same bill cannot be charged twice for overlapping days, and interest is not calculated on interest charges.
- The charge is a new `InterestCharge` customer bill (debit receivables, credit a custom income ledger). It appears in outstanding bills, ageing, statements, receipt allocation and credit checks.
- Corrections use the normal reversal, which is blocked while settlements remain.

### Standard costing (item 4)

- **Migration to standard.** Existing stock moves to standard cost, or to a revised standard, today. Each stock identity is revalued to quantity × standard. The difference posts to a reviewed income or expense ledger, and the new policy version is recorded in the same transaction. Earlier stock documents cannot then be reversed.
- **Leaving standard.** A product can leave standard costing prospectively for average or FIFO once its allowance is released.
- **Impairment allowance.** Standard-cost impairment goes to a contra-asset allowance ledger. Later reviews raise or release it, including when goods are sold, and standard carrying values stay unchanged. The market write-down now skips standard-cost items instead of failing.

### GST (item 1)

- **Stock input tax after goods have moved.** A preview splits the reviewed GST by product:
  - **Standard-cost goods:** reduce the purchase price variance (5100), where their price difference was expensed.
  - **Other goods on hand:** reduce inventory.
  - **Goods already issued:** the reviewer confirms this portion (the preview proposes it by quantity), and it reduces cost of sales or a chosen expense.

  Claims without later issues behave as before.
- **Corrections to recorded GST returns.** A reviewed, immutable correction against a recorded month is posted in the current period:
  - additional tax is paid from cash/bank or met from carried credit;
  - reductions become carried credit;
  - penalties and interest are expensed.

  The original return and its locked month stay unchanged.

Submission to the NRA, and independent confirmation that these treatments match NRA rules, remain external.

### Company periods (item 2)

- **Retrospective split.** When activity after the cutoff already exists and the owner confirms it:
  - the continuation keeps that activity open;
  - the archive holds a read-only copy labelled with the count of later documents.

  Protected history is never rewritten.
- **Coordinated synced-branch split.** A synced branch can be split once head office holds everything: nothing pending, no unreviewed conflicts, and a successful sync within the hour. Only the continuation keeps the branch's sync connection. Archives never sync, and the original's sync settings are set aside.
- **Verified compaction.** Takes a safety backup, runs SQLite `VACUUM`, then requires a passing integrity check, unchanged record counts and an unchanged protected-history head. It also works on archives.

### Migration, reporting and search (item 6)

- **Opening balances import.** A new import posts ledger balances and open customer/supplier bills as at one date, against opening balance equity. Opening bills are settled, aged, interest-charged and stated like other bills. The outstanding-bill direction now uses the shared receivable rule.
- **Tally XML masters.** The adapter now converts a company's masters as well as plain vouchers:
  - ledgers, resolved through the file's group hierarchy;
  - customer and supplier ledgers, which become parties;
  - stock items and opening stock;
  - opening balances, including bill-wise customer and supplier bills.

  Import in that order. Tally does not hold everything Rokela needs: missing selling prices are left empty for review, and codes are generated.
- **Tally XML export.** Exports ledger masters and posted ledger entries for a period, for external reporting tools. The export round-trips through the importer for plain vouchers; cost-centre and inventory details are not exported.
- **Live company search (desktop).** Searches current records in every company on the computer, including archives, where the signed-in user has an owner account. Companies without one are listed as skipped, and nothing is exported.

### Housekeeping

- Sync protocol is now **4** because of the new document kinds and records. Head office and every branch must upgrade together.
- Patched `System.Security.Cryptography.Xml` and `System.Security.Cryptography.Pkcs` to 10.0.13, with transitive pinning. Earlier versions carried high-severity advisories that fail the build because warnings are treated as errors.

## Verification

Run on Linux with the .NET 10.0.402 SDK, 16 October 2026:

- **Baseline before these changes:** 399 passed, 0 failed, 12 skipped.
- **Full Release regression after the changes:** 421 passed, 0 failed, 12 skipped (433 total). The skipped tests are seven live SQL Server/PostgreSQL tests (including the PostgreSQL launcher test), plus four desktop tests and one sync screen test that need Windows.
- **Builds:** the test project, web host and backup worker build with zero warnings and zero errors with NuGet auditing enabled.
- **Not built:** the Windows MAUI app and installer.

Test files: `ConsolidationTests`, `AdvancedAccountingTests`, `AdvancedAccountingScreenTests`, `InventoryDepthTests`, `ComplianceTests`, `TaxScreenTests`, `CompanyPeriodTests` and `TallyMigrationTests`.

## Still outside source work

These require hardware, credentials, live services or a Windows machine. They cannot be completed or verified from source changes alone:

- **Live Google Drive:** sign-in, upload and scheduled backup task registration.
- **Windows acceptance:** native accessibility, display scaling (DPI), keyboard and visual acceptance.
- **Hardware:** printers, scanners, cash drawers, cheque alignment.
- **Installation:** clean-machine install, upgrade and rollback.
- **Networks:** two-device network outages.
- **Live databases:** configured SQL Server and PostgreSQL checks.
- **Release:** code signing and distribution; a new installer containing this work.
- **Tax:** NRA submission and independent jurisdiction acceptance of the GST treatments.
- **Deferred connectors:** bank, fiscal-device, email and WhatsApp.
