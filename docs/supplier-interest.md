# Supplier interest and coordinated Windows release

The reviewed interest worksheet supports supplier and customer bills. For a supplier, select Supplier payables, calculate the agreed interest, choose a non-control expense ledger, and review the amount, date and reason before posting. The charge debits expense and credits payables. Use the resulting bill to allocate a payment or eligible credit. Reverse an active settlement before reversing the charge. The worksheet does not compound interest on interest bills. Posting recalculates eligible interest inside the same serializable transaction as the posting and validates overlapping charged periods.

The existing API field IncomeAccount is preserved for compatibility; on a supplier request it identifies an expense account. No database migration is required. Sync protocol 5 introduces a new supplier interest document kind: upgrade head office and all branches together.

For external acceptance still required, see [the release pipeline](windows-release-pipeline.md) and [implementation scope](remaining-implementation-2026-10-16.md). A release tag starts the Windows build and clean-machine checks; publication is conditional on those jobs succeeding.
Local Release verification: 20 accounting, rendered-screen and sync compatibility tests passed, with no failures or skips. The supplier test posts a charge, rejects the wrong ledger and overlapping period, retries without duplication, pays it and reverses the payment and charge.
