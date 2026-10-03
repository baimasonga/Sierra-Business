# Banking and cash control — 0.5.0

This release adds offline bank statement review, received-cheque tracking, cashier till closing, and paid-purchase refunds/credits. It does not provide complete Tally parity or connect to a bank.

## Bank statements

Open Banking. Select a cash/bank ledger and enter the statement identifier, opening and closing balances. Load or paste a UTF-8 CSV with exactly `Date,Reference,Amount` columns. Dates use `yyyy-MM-dd`; amounts use a decimal point, with receipts positive and withdrawals negative. Quoted references containing commas are supported. Limits: 1 MB and 5,000 rows. Preview the rows before importing. Opening plus transaction total must equal closing.

Import creates statement records, not accounting entries. Identical parsed content or an existing identifier for the same branch/account is rejected. Every match is explicitly selected and confirmed. Suggestions use the exact signed amount and a book date within the preceding 30 days. Other same-amount entries are also selectable. There is no automatic posting of bank fees or arbitrary bank-format mapping. Cancel incorrect matches in Accounting; then rematch. A statement with active matches cannot be voided. Void history is retained.

## Received cheques

New cheque sales register automatically. Banking also receives cheques against outstanding credit invoices or bounced-cheque debts. Cheques start in account 1020 (in hand), move to 1030 (deposited awaiting clearance), and reach the chosen bank ledger only on clearance. Bounce removes the instrument from its current account and creates a receivable bill. Settle that bill using Accounting, another received cheque, or a customer credit.

Each action needs a date and reason/bank reference. Postdated cheques cannot be deposited early. Stale versions are rejected. Undo reverses the latest uncorrected action with an immutable history; allocations against a bounce must be reversed first. Source receipts/sales cannot be reversed until later cheque actions have been undone. This release tracks received cheques, not issued-cheque printing or electronic bank submissions.

Earlier-version cheque sales can be selected for registration without reposting their journal. Register only instruments still held and represented by the existing in-hand balance; review historical manual cheque postings first. Legacy returns directly credited against cheques in hand must be corrected before registration. The system does not infer bank clearance from those earlier manual entries.

## Cashier shifts

Open Tills and count the opening cash. Standard cash sales use account 1000. One open shift is allowed for a cashier and for each ledger in the branch. All journal movements to that ledger during the open shift are captured, including other staff, refunds, reversals and backdated postings entered during the shift. This is a shared ledger till, not a separate cash drawer automatically assigned to each salesperson.

Expected closing cash is opening physical count plus captured movement. Opening book balance and its difference from the physical count are shown separately. Refresh before closing, enter the actual count and a note; changed net activity rejects stale closing. Closed counts and their movements are immutable. Later postings belong outside that closed shift. A shortage/overage is recorded for investigation; it does not automatically become an expense/income voucher. `tills.use` permits own shifts; `tills.manage` permits reviewing/closing others. Existing owners receive these permissions on upgrade; assign other existing staff explicitly.

## Paid purchase returns

Returns now reduces any unpaid supplier bill first. For the already-paid portion, choose a cash/bank refund actually received or a supplier credit in account 1300. Under Banking, a supplier credit can be partially refunded into a cash/bank ledger or allocated to an outstanding bill for the same supplier. Overuse/overpayment is rejected. Reverse downstream credit uses before reversing their original debit note. Printed debit notes and supplier-credit-use records retain the posted settlement.

## Delivery and remaining acceptance

The Windows package remains a single self-contained installer for the .NET 10 MAUI application. Native printer/scanner hardware, company switching, and clean-machine installation/upgrade require acceptance checks. No live user database was migrated during development. See implementation-progress.md for remaining non-manufacturing scope.
