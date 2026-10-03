# Business workflows preview 0.3.0

This release expands the offline .NET 10 / MAUI Windows application. It does not yet implement every non-manufacturing Tally feature. See [implementation progress](implementation-progress.md) for the explicit remaining scope.

## Working with the new modules

1. Back up the existing company from the sidebar and close its window before installing the update.
2. Open **Customers & suppliers** to enter customer terms, credit limits and supplier contacts. Unregistered legacy party names remain supported without those credit controls.
3. Use **Orders & quotations** to create quotations and orders, convert quotations, and invoice/receive agreed quantities in parts.
4. Use **Warehouse controls** for additional stores, transfers and quantity-item stock counts. **Returns** credits part or all of an original transaction; the system checks outstanding settlements and stock value.
5. In **Accounting**, post balanced vouchers, settle bills, inspect ageing, review statements and match bank entries. Custom ledgers, cost centres and budgets are available.
6. In **Payroll**, create employees and post fixed full-month payroll. Review payslips before printing; payment allocation and statutory calculations are not automatic.
7. In **Users & permissions**, create staff accounts and assign only their required permissions. Permission changes invalidate existing sessions. The password reset control is available in this screen.
8. In **Periods & tax**, set future sales-tax rates or close a period after taking a backup. Closing is irreversible in this preview. Tax configuration is generic, not a statutory filing service.
9. In **Documents**, open a sale or goods receipt for its printable preview. Quotations/orders and payslips also have **Print / save PDF** buttons. Select a configured PDF printer in the print dialog to save a file. Native printing needs hardware acceptance testing.
10. Use **Change company** in the Windows application to create/open an isolated company. Each company has separate records and user accounts.

## Verification and limits

The 77-test suite includes real SQLite transactions, migration from 0.2, rounding/returns, credit controls, user security, period locking and escaped printable invoices with posted prices. HTTP verification covers the authenticated endpoints and antiforgery rejection. These tests do not establish full native UI, printer, scanner or clean-machine acceptance.

This is an unsigned development preview. Central synchronization, SQL Server migration support, statutory payroll/tax integrations, multi-currency, approvals, advanced POS/inventory, complete exports and complete tender/equipment/service workflows remain unfinished. Offline OCR and optional consent-gated online assistance from 0.2 remain included.
