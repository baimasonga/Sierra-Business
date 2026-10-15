# Remaining backlog implementation — 11 October 2026

Authorization: implement all remaining features described after Windows 0.32.0. Bank, fiscal, email and WhatsApp connectors remain deferred under the earlier user instruction. Android and manufacturing remain excluded.

This is ongoing source work after the immutable 0.32.0 release. The 0.32.0 installer does not include these changes. Do not interpret this document as completion of the whole backlog.

## Implemented source workflows

- Reusable cost-centre allocation classes, version checks, reviewed preview and posting, rounding reconciliation and identity-bound retry handling.
- Parallel cost categories: immutable classifications of existing documents; each category independently reconciles revenue and expenses, includes unallocated activity and follows source cancellations. Category totals must not be added together.
- Commercial projects: distinct centre/code identity, project dates, revenue/expense budgets, centre activity and explicit assignment of trading/expense documents. Assignments take precedence over legacy centre codes; cancellations follow their original assignment. Inventory acquisition remains an asset until the relevant expense is recognized.
- Scheduled journals and optional opposite journals: non-posting schedules, reviewed due posting, independent reversal-stage posting, period locks, immutable posted schedules and duplicate prevention. A posted opposite journal must be cancelled before cancelling its original. Posting is reviewed, not an unattended scheduler.
- Expense input-tax recovery: a tax-registered company, source expense payment, invoice/customs identifiers, paid GST and reviewed allowable GST are required. Only a source expense line equal to purchase plus GST can be reclassified; credits cannot exceed tax paid. Duplicate payment/invoice claims and reversal of a source with an active claim are blocked. The net GST worksheet/export separates local/imported amounts and subtracts allowable inputs from recorded output GST. Subsequent increments add direct stock-receipt input recovery before any later stock issue, invoice-specific recipient relief certificates with branch ceilings, and owner-reviewed monthly GST records with snapshot checks, ledger settlement, carried credit, reviewed rounding and period locking. Cancellations enter their own tax periods. Authority submission, post-issue stock recovery and later filed-return corrections remain outstanding.
- Exact reciprocal intercompany-account proposals. Expense/income or asset/liability pairs must match exactly; discrepancies require investigation. Proposals require review before inclusion in group reporting and never post to member books.
- Portable company search indexes: owner-authorized export of document references, products, parties and ledgers; search up to twenty explicitly loaded indexes with source lineage and export dates. This is a snapshot search, not automatic access to other companies or a live cross-company query.
- JSON exchange adapter for all existing import types using Version 1, Type and scalar Rows keyed by the templates. Tally XML adapter for plain journal/payment/receipt/contra vouchers using uniquely named existing ledgers. Both produce review workbooks and use existing mapping, validation and atomic commit. XML entities/DTDs are prohibited; inventory, bill allocations, cost-centre details, optional/cancelled vouchers and foreign amount syntax are refused rather than discarded. This is not complete Tally or complete-company migration.
- Prospective weighted-average/FIFO transitions for existing stock, dated today, preserving current values and sealed history. Earlier stock documents cannot be reversed across a transition; use current reviewed returns/adjustments. Standard-cost transitions still require a separate migration/allowance design.
- Standard-cost foreign supplier receipts and invoice-later matching. Price differences are expensed while stock retains standard value; rate differences remain separate. Existing non-standard matching behavior is retained.
- Reviewed current-date stock write-downs for weighted-average/FIFO stock. Quantity is unchanged, upward values are capped, stale book totals are rejected, expense/inventory entries balance and corrections use reversal. Standard-cost impairment requires a separate allowance policy.
- Closed-app backup executable and Windows task-installation script. The worker follows existing automatic settings/retention/copy folder, acquires the company lock, verifies history, refuses pending migrations and skips an open desktop. The task runs as the current limited Windows user while signed in. No task has been registered against live company data in this implementation run. The packaging script bundles the worker separately.

## Verification evidence

- Initial accounting run: 11 passed, no failures/skips.
- Accounting/compliance run: 18 passed, no failures/skips.
- Accounting/compliance/backup run: 30 passed, no failures/skips.
- Consolidated run before the latest source changes: 62 passed, no failures/skips.
- Inventory/search/backup run before final project-assignment and write-down additions: 41 passed, no failures/skips.
- The backup worker built without warnings/errors. The task-installation script passed PowerShell syntax parsing.
- An overlapping test build encountered a locked assembly; testing was subsequently sequenced. Backup-screen stand-in dependency handling and a consolidation test tied to 10 October were corrected. Record final full-suite results separately after completion.

## Still requiring implementation or deeper completion

1. Post-issue inventory input-tax allocation, standard-cost tax treatment, later filed-return corrections and independent jurisdiction acceptance.
2. Retrospective company partitioning, verified physical compaction and coordinated synced-branch period migration.
3. Ownership/minority-interest consolidation and functional-currency translation; broader transaction-level intercompany matching.
4. Standard-cost migrations for existing stock and impairment allowance policy.
5. ~~Reviewed billable interest charges~~ — implemented 15 October 2026; see [customer interest charges](interest-charges.md). Customer/supplier currency advances, currency-bank refunds/revaluation and local cheque advances are implemented in source and covered by focused regression tests.
6. Complete-company and broader Tally migration, external reporting compatibility and live cross-company search.
7. Live Google OAuth/upload acceptance, credential provisioning and scheduled-task operational acceptance. Google Drive integration is implemented in source: PKCE browser sign-in, encrypted chunked backups, scoped resumable uploads, verified metadata and reviewed encrypted import. No live sign-in, upload or scheduled-task registration has occurred.
8. Native accessibility/DPI/keyboard and hardware acceptance; clean-machine install/upgrade/rollback; two-device outages; live SQL Server/PostgreSQL checks; signing and distribution.

## Primary references consulted

- [NRA GST return](https://www.nra.gov.sl/sites/default/files/GST_Return.pdf): output GST, local/imported inputs, allowable input tax and net payable/credit working-paper structure.
- [NRA guide to GST legislation](https://www.nra.gov.sl/sites/default/files/Sierra-Leone-A-Guide-to-the-GST-Legislation.pdf): allowability requires review; the software does not assume every paid tax is deductible.
- [Tally XML sample](https://help.tallysolutions.com/sample-xml/): accounting ledger entries and debit/credit conventions used by the limited XML adapter.

Live connectors remain deferred. Building or exporting a working paper does not submit a tax return, initiate a bank transfer or certify statutory compliance.

## Subsequent source verification
- Full Release run before the later Google Drive, currency-advance and GST additions: 392 passed, 6 external-provider tests skipped. This is not verification of the final current source.
- Google Drive and related focused suite: 73 passed. Currency/cheque advances and banking regression: 46 passed. Purchase-tax periods: 39 passed. Monthly GST: 40 passed. Recipient relief/service/workspace regression: 39 passed.
- Tax and backup screens now protect unfinished work when leaving the workspace. Voucher allocation metadata preserves older plain-text sources; malformed JSON receives a review error. No-charge service jobs reject relief certificates.
- Sync protocol is now version 3 for currency-unit and tax-period semantics; all participating branches/HQ require a coordinated upgrade.
- Current source still requires a final full suite and new immutable release package. The 0.32.0 installer remains unchanged.
- Latest focused GST rounding, service, workspace, backup and advanced-accounting run: 40 passed, 0 failed, 0 skipped; tax-draft-rounding-2026-10-11.trx. The subsequent confirmation-wording edit changes text only.
