# Price rules, banking tools, financial statements and payroll extras — 0.24.0

This release closes the smaller gaps left against the specification.

## Price rules and promotions
**Sales & purchases → Price rules** (editing needs `pricing.manage`).
- A rule takes a percentage off the list price, or sets a fixed price for one product.
- It applies to one product, a product category, or every product.
- It can be limited to one customer type (for example Government or NGO, the *type* on the customer record), to sales from a minimum quantity, and to a date range.
- On **New sale**, **Apply price rules** reprices each line at the best rule. A price at or above the best rule needs no discount approval and is not stopped by the minimum price.
- Bundle lines keep their package price.

## Banking tools
**Finance → Banking tools** has three parts.
- **Statement formats.** Read a bank's own CSV export: choose a built-in layout or save the bank's own (separator, header rows, date format, details column, one signed amount column or separate debit and credit columns). Amounts such as `1,234.50`, `(200.00)`, `15.00 DR` and `1 000.00` are understood, and balance and summary rows are skipped. Preview the rows, then import; matching then happens on the Banking page as before.
- **Cheques issued.** Writing a cheque posts the payment voucher, either against supplier bills or to an expense ledger, and registers the cheque number (one number per bank account). Mark it **Presented** when the bank pays it. **Cancel / stop** an unpresented cheque with a reason, which reverses the payment. Cheques still marked Issued are the unpresented cheques for bank reconciliation.
- **Cash drawers.** Assign cashiers to a cash ledger (`tills.manage`). Once a drawer has cashiers, only they, and till managers, can open it. A drawer with nobody assigned stays open to every cashier.

## Financial statements and year-end close
**Finance → Financial statements** (needs `accounting.read`):
- **Profit and loss** for any period.
- **Balance sheet** at any date, showing retained earnings from closed years and current-year profit, and checking that it balances.
- **Cash flow** (direct method): every cash and bank movement classed as operating, investing or financing, reconciled to the cash and bank ledgers.
- **Funds flow**: sources and uses of funds from the changes in balance-sheet accounts.
- **Year-end close** (`accounting.write`). Give a reason and the year's last day; this must be after that day, and years are closed in order. Closing records the year's profit and locks the period, so nothing more can be posted into the closed year. Closed years cannot be reopened.

Every statement, and every report on the **Reports** screen, has **Export to Excel**.

## Refund approvals
**Controls & exceptions** has a new limit: *refunds and returns above an amount*, set to NLe 500 by the recommended controls. A larger credit or debit note waits on the Approvals page and is posted once it is approved. It is listed in the exception report as **Refund**.

## Payroll extras
- **Leave allowance.** Enter it in the payroll run. By default, leave allowance up to one month's basic salary is tax-free and any excess is taxed at 30%. Both settings are part of the dated PAYE rules.
- **End-of-service and redundancy benefits.** Paid in the run. They are tax-free by default (Finance Act 2026) and never attract NASSIT.
- **Staff loans and advances** (*Loans* tab). A loan is paid out in cash or by transfer to the employee loans ledger (1310). The monthly instalment is deducted in each payroll run until the loan is repaid; tick **Skip loan** to skip a month. An employee can have one loan at a time. A loan cannot be reversed once instalments have been deducted.
- **Bank transfer file.** Add each employee's bank and account number. **Bank transfer file**, under Payslips & payments, downloads a CSV of the selected payslips' net pay for the bank's bulk transfer.
- **Payslips** show leave allowance, end-of-service benefit and loan repayment.

## Currency format
Report and dashboard amounts follow the currency format set under Company & tax.

## Tests
- `SmallerGapsTests` covers price rules, statement layouts, issued cheques, cash drawers, statements and year-end close, Excel exports, refund approvals, and leave, end-of-service, loans and bank files.
- `SmallerGapsScreenTests` drives the three new screens.

269 tests pass.
