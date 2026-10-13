# Remaining-area implementation — 0.32.0

Source work on 10 October 2026 adds company period management, inventory depth, banking productivity, broader onboarding and reviewed group adjustments. This is not a claim of complete Tally parity or statutory certification.

## Company periods

Under Backups & integrity, an owner can review a standalone company's cutoff and create checksum-verified archive and continuation datasets. All stock, bills, payroll liabilities, users, captured originals and protected history are preserved because the database is copied consistently. The original and archive become read-only for business changes; only the continuation accepts new trading after the cutoff. A database write barrier rechecks the history head before publishing the copies, so changes after preview cannot be silently lost.

This is a period cutover with full reference history retained, not destructive database compaction. The cutoff must include every posted document, order, custody and attendance activity date, be at or after the current period lock and be before today. Retrospective partitioning when later activity is already present and coordinated splitting of synced branches are not supported by this release. It never rewrites sealed posted history. Separate company copies require selecting the continuation in the company chooser after closing the old window.

## Inventory

Warehouse controls now has tabs for normal transfers/counts, costing/valuation, and consignment/rejections.

- New products may select weighted-average, FIFO or standard book costing before their first movement. Existing products retain their historical method. FIFO layers carry through warehouse transfers and cancellations. Sales, service issues, purchase returns and shortage counts use the selected cost calculation.
- FIFO and standard-cost items reject posting dates before the policy or later stock activity. Standard goods receipts use local direct supplier bills; inventory uses the configured standard and purchase-price differences go to operating expenses. Landed costs are expensed through cost of sales. Foreign-currency and invoice-later receipts use weighted average or FIFO. Opening/surplus stock for standard items must use the configured standard cost.
- Book, FIFO, LIFO, standard-reference and market-reference comparison reports are available. LIFO is a comparison report only. Reference methods require reviewed unit values; they do not automatically post write-downs. Alternatives leave existing journals unchanged and export to Excel.
- Company-owned goods at a partner remain owned inventory in a registered consignment warehouse. Dispatch/return use ordinary valued warehouse transfers; sales are recorded from that warehouse.
- Supplier-owned goods use a separate custody register. Receiving/returning custody goods creates no inventory asset or supplier payable. Acceptance creates a normal goods receipt for the exact supplier, identity and remaining quantity. Its source is retained; reversed acceptances reopen custody availability.
- Pre-invoice customer rejections restore stock at the delivery's allocated cost, reduce goods delivered awaiting invoice, and reopen the rejected order quantity. The delivery invoice uses only its accepted remainder and does not issue stock again. Later sales returns retain the accepted invoice quantity/cost. Active rejections block reversal of the original dispatch; active accepted-quantity invoices block reversal of rejections.

## Banking and imports

- Reviewed unmatched bank rows can create a payment or receipt and its match in one transaction. Bank, date and signed amount must match. Cancelling a match does not permit a second voucher from the same row while the first voucher remains active.
- Cheque books register bank-specific prefixes/ranges and report the next unused number. Cancelled cheques retain their numbers. Overlapping active ranges and out-of-book issuance are rejected. New cheque issuance commits the cheque and payment voucher together; retries reject altered details or identities.
- Cheque print layouts store millimetre dimensions, coordinates and text size. Preview/bulk printing accepts 1–25 issued, uncancelled, unpresented cheques from the layout's bank. Changed layouts require a new preview. Marked test prints and print-attempt audits are provided. Printing neither initiates a bank transfer nor marks a cheque presented. Physical alignment remains a hardware acceptance check.
- Excel onboarding adds customers/suppliers, custom ledgers and grouped journal/payment/receipt/contra vouchers. Party and ledger imports share manual-entry validation. Voucher previews stage no postings; the entire batch rolls back if any group fails, including combined bill over-allocation. External voucher references prevent re-import under reordered rows. Vouchers requiring approval use the normal Accounting approval workflow.

## Group reports

Group-company reports now support reviewed, balanced elimination/adjustment entries using mapped group accounts. Every entry needs a unique reference, reporting-period date and reason. Their contribution appears separately from member books; both the report and saved reporting set retain them, and Excel includes an Adjustments worksheet. Member journals remain unchanged. Ownership/minority-interest adjustments and functional-currency translation are not automated.

## Verification and release

Verified on 11 October 2026: the focused 25-test run passed; the packaged source's full Release regression passed 380 tests with zero failures and six skipped live SQL Server/PostgreSQL tests (386 total). Authenticated HTTP workflow checks and the offline Windows capture/OCR smoke check passed. Native Windows publish and Inno Setup compilation succeeded. The installer passed all 26 release artifact checks, including its checksum and immutable source manifest.

The unsigned installer is `artifacts/windows/0.32.0/RokelaBusinessAdmin-Setup-x64.exe`. SHA-256: `B9238A83D8999BEDDA41F4F1DC0BED305A507CB70D9A5D8F91F9BB19E712FF82`. Machine-readable evidence is in `artifacts/windows/0.32.0/release-verification.json`; full test evidence is in that release's `tests/tests.trx`.

Execution policy blocked the isolated UI-preview startup command without a specific reason. Visual/native acceptance and clean-machine installation remain unverified; successful compilation and HTTP checks do not establish those results.

Physical printer/scanner/drawer testing, a clean external machine, two-device network acceptance, configured live database-provider checks, signing credentials and distribution remain external dependencies unless separately verified. No live bank or fiscal connector is activated by these changes.
