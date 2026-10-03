# Supplier invoices, foreign-currency purchase orders and credit sales — 0.13.0

Completes specification **Skill 13**: Purchase Order → Goods Receipt → Supplier Invoice → Supplier Payment, while keeping the simplified local purchase entry (a receipt that is also the bill).

## Two ways to receive goods
- **Receipt is the bill (unchanged).** Post the receipt as before; it creates the supplier payable straight away.
- **Supplier invoice to follow (new).** Tick *Supplier invoice to follow* on **Receive stock**, or when receiving against a purchase order. Stock is valued at the receipt prices, and the amount goes to a new standard account, **2050 Goods received not invoiced**, instead of Trade payables. Nothing is payable until the supplier's invoice is matched.

## Matching the supplier's invoice
**Sales & purchases → Supplier invoices** (needs `accounting.read`; posting needs `accounting.write`).
1. The first table lists goods still waiting for an invoice, by supplier, with the oldest age in days and the NLe value in account 2050.
2. Choose the supplier and enter the supplier's invoice number and date. The due date is optional; when it is left empty, the supplier's payment terms apply.
3. Tick the receipt lines on the invoice and enter the quantity and amount exactly as billed. Partial invoices are allowed, and one invoice can cover several receipts. The amount is pre-filled from the receipt price.
4. **Check against receipts** shows, for each line, the expected amount, the invoiced amount and the price difference, and how that difference is split.
5. **Post supplier invoice** creates the supplier bill. Settle it with a payment voucher under Accounting. If it is in a foreign currency, pay it under Imports & landed cost → Foreign-currency bills.

Rules:
- An invoice cannot bill more than the quantity still waiting, cannot be dated before the goods were received, and cannot reuse a supplier invoice number already recorded for that supplier.
- An invoice is in one currency, the currency of the receipts it matches.
- If any amount differs from the receipt price, a **reason** is required.

Accounting:
- **Debit 2050** for the receipt value of the goods invoiced.
- **Price difference:**
  - the share for units still on hand goes to **1200 Inventory**, as a value-only stock adjustment;
  - the share for units already sold goes to **5000 Cost of sales**.
  - A price reduction never takes stock value below zero.
- **Exchange-rate difference** between the receipt date and the invoice date (foreign currency only):
  - a loss goes to **5300 Exchange losses**;
  - a gain goes to **4200 Exchange gains**.
- **Credit 2000 Trade payables** for the invoice amount in NLe, at the invoice date's rate.
- The last invoice for a receipt line clears the exact remaining amount in 2050, so no rounding cents are left behind.

## Foreign-currency purchase orders
- Purchase orders can be raised in a currency that has an exchange rate, for example USD. Prices on the order are in that currency.
- Receipts against the order must be entered in the same currency at the order price. Each receipt is converted to NLe at its own date's rate.
- Quotations and sales orders remain in NLe.

## Returns and reversals
- **Goods not yet invoiced** are returned against their receipt with *Original settlement*. The return reduces 2050; there is no refund, because nothing was paid.
- **Goods already invoiced** are returned as a **supplier credit**, or a cash/bank refund. Then apply the credit to the supplier's invoice under Banking → Supplier credits.
- Return uninvoiced and invoiced units on separate return documents.
- A receipt with matched supplier invoices cannot be reversed until those invoices are reversed.
- A supplier invoice cannot be reversed once:
  - it has payments or credit allocations; reverse those first; or
  - the stock it adjusted has moved, for example after a later sale. Record a correcting document instead.
- The same applies to any value-only adjustment, including a price reduction (previously only increases were protected).

## Credit sales need a registered customer
A credit sale, meaning a sale with no payment taken, is now refused unless the customer is registered under **Customers & suppliers**. Before this, a typed-in name skipped the customer's credit limit and credit hold. Cash, cheque and bank-transfer sales to unregistered names are unchanged.

## Database
Migration `SupplierInvoiceMatching` adds:
- the `InvoiceMatches` and `OrderCurrencies` tables, both protected by the database guards and the integrity seal chain;
- standard account 2050 *Goods received not invoiced*;
- the document kind *SupplierInvoice*.

`TradeCommand` gains `InvoiceLater` and `OrderCommand` gains `Currency`; older stored requests remain valid. An order's currency is kept in its own table, so the seals on existing orders are unchanged.

## Verification
196 tests pass. New tests cover:
- partial invoices;
- a price increase split between stock on hand and cost of sales;
- a cheaper invoice;
- a foreign-currency invoice with separate price and exchange differences, paid in its currency;
- returns before and after invoicing, including a supplier credit applied to the invoice;
- foreign-currency purchase orders;
- duplicate invoice numbers;
- reversal guards;
- the credit-sale rule.

Every scenario checks that debits equal credits and that the inventory ledger equals stock value; the integrity chain is verified after each one. A rendered-screen test drives matching and posting through the Supplier invoices page, and the browser host was checked visually. Existing tests that sold on credit to unregistered names now register the customer first.

## Limits
- There is no automatic tolerance for small price differences: any difference needs a reason.
- There is no approval step for large differences.
- Supplier invoices for services (not goods) are still recorded as payment vouchers or landed cost documents.
- Open foreign-currency bills are not revalued at period end.
