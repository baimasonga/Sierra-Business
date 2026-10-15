# Customer interest charges

Source work on 15 October 2026. Completes backlog item 5 ("reviewed billable interest charges") from [the remaining backlog](remaining-backlog-implementation-2026-10-11.md).

## Charging interest

1. Open **Accounting**, choose the **Advanced accounting** view and its **Interest** tab, enter the agreed annual rate, grace days and day-count basis, keep **Customer receivables** selected and calculate the worksheet. Calculating never posts anything.
2. Users with `accounting.write` see a **Charge** box on each row. Tick the bills to charge for **one customer**, choose the charge date (on or after the worksheet's end date, in an open period) and the payment terms in days.
3. **Charge selected interest** recalculates the interest on the server. If the amount no longer matches the worksheet (for example because a payment was recorded meanwhile), nothing is posted and you are asked to recalculate.

The charge is an **interest charge** document for that customer:

| Ledger | Debit | Credit |
|---|---|---|
| 1100 Trade receivables | interest | |
| 4400 Interest income | | interest |

It is a customer bill like a credit invoice: it appears in outstanding bills, ageing, statements ("Interest charge") and collection reminders, and receipts are allocated to it in the usual way. **Print** produces an *Interest charge note* listing each overdue bill, its days and interest, the rate, the period and the payment due date.

## Safeguards

- **No double charging.** Once a bill's interest is charged through a date, later worksheets only calculate interest after that date.
- **No compounding.** Interest charges never bear interest themselves.
- **Cancellation.** Reverse the interest charge from the document list. A charge that has receipts allocated cannot be reversed until those receipts are reversed. Reversing the charge reopens its period, so the interest can be charged again.
- **Retries are safe.** Repeating the same charge request returns the original document.
- Supplier payable worksheets remain calculations only: interest owed to suppliers is not posted automatically.

## Upgrade notes

- The upgrade adds ledger account **4400 Interest income** (SQLite, SQL Server and PostgreSQL migrations `InterestCharges`).
- **Sync protocol 4.** Branches and head office must be upgraded together: older releases cannot interpret interest charge documents or the new ledger. Unsynced local changes stay queued until every site is upgraded.
