# Negotiated sales and fulfillment — 0.4.0

## Agreed prices with a confidential minimum

Pricing follows bargaining, not mandatory retail/wholesale tiers. Catalogue prices are indicative asking prices. Staff enter the agreed selling price; the server rejects a price below the authorized minimum per base unit, before tax. Unit conversion cannot bypass the check. Quotations, sales orders, delivery notes and invoices all enforce it. Lowering an asking price or making a discounted offer never overrides the minimum.

Only owners or users explicitly granted pricing.manage can view/change minimum prices in Sales controls. Ordinary product views, API responses and customer printouts do not contain them. Rejection messages do not disclose the limit. Changes are recorded as immutable policies with audit operations. Authorized owners can delegate this permission in Users & permissions; the ordinary sales-clerk preset does not include it.

**Setup required after upgrading:** no business minimums are guessed. Existing products and newly created products without a configured minimum cannot be quoted/sold until an authorized manager sets one. Existing saved records and idempotent transaction retries remain available. Purchase receipts do not require a minimum selling price.

## Delivery, reservation and invoicing

1. Create the sales order with the customer-agreed prices.
2. In Sales controls, reserve quantities against the order, warehouse and serial/batch/expiry identity when needed.
3. In Orders & quotations, select **Deliver now; invoice later** and post the quantities actually being delivered. Alternatively, retain immediate invoicing.
4. Print the delivery note from Documents. It contains goods, quantities, tracking details and receipt-signature space, without prices.
5. In Sales controls, invoice the pending delivery. This records revenue, tax and payment/receivable without a second stock issue.

Reservations protect against unrelated sales, transfers, purchase returns and stock reductions. Matching issues consume reservations; manual release or closing the order frees the remainder. Reversing an issue does not automatically recreate its reservation; reserve again if needed.

Orders support multiple partial deliveries. Each delivery is invoiced in full once; use separate delivery notes for staged invoices. Delivered stock value is held in a separate ledger until invoiced. Reverse a linked invoice before reversing its delivery; invoice reversal alone does not claim the goods came back. Expiry checks apply to dispatch, not later invoicing of already-delivered goods.

## Paid returns, refunds and customer credits

On a sales return, choose cash refund, bank refund or customer credit. Any unpaid invoice balance is credited first; the paid portion is refunded or retained as a customer liability. Customer credits require a named customer. From Sales controls, refund part/all of a credit or allocate it to an outstanding credit invoice belonging to that same customer. The service prevents over-refunds and over-allocation and retains reversal/audit history.

Cash/bank refunds record accounting entries; they do not initiate an external bank transfer. Customer-credit use needs accounting.write. Purchase returns still require an unsettled supplier balance; supplier overpayment/refund workflows are outside this sales-return change.

## Printable documents

- Sales quotations/orders can print as **Proforma invoice**, with agreed prices, net totals and the existing note that tax is calculated on invoicing. A proforma is not a tax invoice/payment receipt.
- Posted sales print as invoices; settled sales can print as payment receipts.
- Delivery notes omit commercial prices and include serial/batch details.
- Sales credit notes, purchase debit notes, receipt/payment vouchers, customer refunds and credit allocations have printable views.
- Existing payroll and financial report printing remains available.

Use Print / save PDF and select the configured printer/PDF destination. These templates have automated rendering checks, but physical printer/native-dialog acceptance remains outstanding.

## Validation

All 91 integration tests passed, including the repeated final packaging gate. Tests use isolated databases and configured test minimums. Coverage includes hidden minima and permission denial, converted-unit price checks, missing-policy rejection, immutable price history, reservation consumption/protection, delivery/invoice stock-once posting, reversal ordering, delivered stock valuation/returns, partial paid refunds, credit allocation/refund limits, historical upgrade permissions and credit balances, and printed delivery privacy. HTTP checks exercise the same workflows through authenticated endpoints. Packaging status is recorded in artifacts/windows/0.4.0/build.log; only a successful completion establishes an installer.

This completes this iteration's negotiated-price, sales-delivery/reservation, paid-sales-return and printable-document workflows. It does not establish full Tally parity; the broader remaining modules are listed in implementation-progress.md.


Final installer: artifacts/windows/0.4.0/SierraBusiness-Setup-x64.exe. Packaging and independent checksum verification succeeded. Back up records and close the application before updating. Configure confidential minima after signing in.
