# Purchasing, imports and landed cost — 0.12.0

Implements specification **Skill 13 — Purchasing, imports & landed cost** (roadmap Phase 4 items: foreign currency, import/landed cost; bank and cheque reconciliation and financial statements already existed). NLe (SLE) remains the functional currency: stock, journals and reports are always in NLe.

**Sales & purchases → Imports & landed cost** (needs `accounting.read`; forms need `accounting.write`; landed cost also needs `inventory.receive`).

## Exchange rates
- Effective-dated reference data: *NLe per one unit* of a currency (up to four decimals), with its source (for example the Bank of Sierra Leone mid-rate).
- Each document uses the latest rate on or before its own date. Rates are history: a new rate never changes earlier documents. A date can have only one rate per currency; rates can be entered at most 31 days ahead.
- Anyone who can receive goods can see rates; only accountants can add them.

## Foreign-currency goods receipts
- **Receive stock** shows *Supplier invoice currency* once a rate exists. Enter costs in that currency; each line is converted to NLe at the receipt date's rate. Stock value and the supplier payable are in NLe; the document keeps the currency, rate and foreign total.
- The receipt becomes a **foreign-currency bill**, owed in the supplier's currency.
- Not yet supported: foreign-currency receipts against purchase orders (orders are in NLe). Receive without the order, or keep the order in NLe. Sales are always in NLe.

## Paying foreign-currency bills
- Under **Foreign-currency bills**, choose *Pay…*, enter the foreign amount settled and the **NLe actually paid** (what the bank debited), the cash/bank account and the transfer reference. The screen shows the effective rate and the exchange difference before you save.
- Accounting: **Dr 2000 Trade payables** (the carrying amount at the bill's rate) / **Cr cash or bank** (NLe paid); the difference goes to **5300 Exchange losses** or **4200 Exchange gains** (new standard accounts).
- Partial payments are supported. The final payment clears the exact remaining NLe carrying amount, so no rounding cents are left on the payable.
- Foreign-currency bills cannot be settled with an ordinary NLe payment voucher or supplier credit, which would lose track of the foreign balance. Reverse a payment under Documents if needed; the bill reopens.

## Landed cost
- A landed cost document records a service provider's charges for bringing goods in: freight, insurance, clearing, customs duty, port charges, bank charges, local transport or other.
- Choose one or more goods receipts and how to share the costs: **by value**, **by quantity**, or **manual NLe amounts per line**. Automatic sharing puts any rounding cent on the largest line. *Show allocation* previews the result before posting.
- **Units still on hand** receive the cost in their stock value (a value-only stock movement), so later sales carry it automatically through average cost. **Units already sold** have their share charged to **5000 Cost of sales**. For each product, warehouse and batch/serial, stock on hand up to the quantity received counts as on hand; the rest counts as sold.
- Settlement: **owed to the provider**, which creates a supplier bill payable with an ordinary payment voucher, or **paid now** from a cash or bank account. Charges in a foreign currency are always recorded as owed and become a foreign-currency bill.
- Accounting: Dr 1200 Inventory (on-hand share) + Dr 5000 Cost of sales (sold share) / Cr 2000 Trade payables or cash/bank.

## Reversals and returns
- A goods receipt with landed cost cannot be reversed until its landed cost documents are reversed. A landed cost cannot be reversed once the affected stock has moved (for example after a later sale); correct it with a further adjustment instead.
- A bill with foreign-currency payments cannot be reversed until those payments are reversed.
- A later document that was itself reversed no longer counts as "later activity" when reversing an earlier receipt, because the pair leaves stock unchanged.
- Purchase returns of a foreign-currency receipt are valued in NLe at that receipt's own rate. The bill credit also reduces the foreign balance owed.

## Database
Migration `ForeignCurrencyLandedCost`: `ExchangeRates`, `ForeignBills`, `ForeignSettlements`, `LandedCostAllocations` (all protected by database guards and the integrity seal chain), standard accounts 4200 *Exchange gains* and 5300 *Exchange losses*, and the document kind *LandedCost*. `TradeCommand` gains an optional `Currency` (older stored requests remain valid).

## Verification
185 tests pass, including the Skill 13 acceptance criteria: foreign-currency purchase, partial goods receipt against a purchase order, landed-cost allocation (value, quantity, manual, rounding, on-hand versus sold split, foreign-currency charges, cash-paid charges), supplier payment with exchange loss then gain and exact clearance, and purchase return of a foreign receipt. Every scenario checks that debits equal credits and that the inventory ledger equals stock value. The integrity chain verifies after reversals. A rendered-screen test drives rate entry, foreign bill payment and landed cost posting against the real services, and the browser host was checked visually.

## Limits and next steps
- Separate supplier-invoice matching (goods received not invoiced, price variance) is not yet implemented; a goods receipt is still the supplier bill. Purchase orders remain NLe-only.
- Weight or volume sharing is not available, because products do not record weight or volume yet.
- Revaluation of open foreign-currency bills at period end (unrealised exchange differences) is not performed; differences are recognised when bills are paid.
