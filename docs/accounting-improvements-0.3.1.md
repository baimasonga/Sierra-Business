# Accounting improvements 0.3.1

## New workflows

- Accounting → Cash flow: opening and closing cash/bank balances with net receipts/payments per voucher. Internal cash/bank transfers cancel out; credit invoices and unbanked cheques do not count as cash. Reversal vouchers remain visible as offsetting movements. This is a cash movement report, not an operating/investing/financing classification.
- Accounting → Bank reconciliation → Statement match history: cancel an incorrect match with a required reason, then match the entry again. Original matches and cancellation records remain immutable. Posted ledger balances do not change. Historical reports use the latest corrected matches, rather than reproducing the state of knowledge at the original report date.
- Print / save PDF controls for financial statements and cash flow, using the existing print dialog. Hardware/native printing acceptance remains outstanding.

## Data and validation

An additive migration creates immutable cancellation records and permits multiple historical matches per journal line. The service serializes commands, permits only one uncancelled match, rejects stale duplicate cancellations and preserves retry idempotency. Audit operations retain the actor and time. Correction records can be added after period close because no accounting journal is changed.

80 integration tests passed before packaging, including cash-flow reconciliation, cancellations/rematching, immutable correction history and current permission/branch checks. Extended authenticated HTTP checks also passed match cancellation and cash-flow reconciliation. EF reports no pending model changes. The packaging result is recorded in artifacts/windows/0.3.1/build.log. Existing user data is not used by tests.

Full non-manufacturing Tally parity remains unfinished; see implementation-progress.md. Bank statement import, cheque lifecycle, classified cash/funds-flow statements, central sync, statutory integrations and the other listed modules remain outstanding.


Packaging completed successfully; the installer checksum was independently verified. Output: artifacts/windows/0.3.1/SierraBusiness-Setup-x64.exe. Back up records and close the application before updating.
