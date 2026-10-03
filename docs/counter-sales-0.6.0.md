# Counter sales — 0.6.0

The New sale screen now supports offline hold/recall, barcode/SKU entry, quick product filtering, and editable agreed line prices. Existing confidential minimum prices, stock/tracking controls, receipt printing and till recording still apply.

## Daily counter workflow

1. Open Tills and count opening cash if you use a counted shift. Standard cash sales post to ledger 1000 and join its open shift automatically. All postings to that ledger while open are included, regardless of salesperson.
2. Open New sale. Focus Scan barcode or SKU with Alt+B, scan/type the code and press Enter. USB scanners must operate as keyboard input with an Enter suffix. Leading zeroes are preserved. Quantity products add one scanned unit at the current asking price; pack codes apply their configured base-unit conversion.
3. For serial/batch/expiry products, a scan selects the product; enter tracking details and choose Add line. A second scan cannot overwrite an unfinished selected product. Product name/SKU filtering is available with Alt+K.
4. Edit the agreed unit price directly in the document lines. For a pack, this is the price per entered pack. Confidential minimum checks still apply per base unit at final posting. The floor is not displayed to a salesperson without pricing permission.
5. To pause bargaining, enter a hold label and choose Hold sale (Alt+H). The sale is saved in this company's local database. Stock, reservations, balances and till cash are unchanged.
6. Open Held sales and Recall the chosen customer. Active held sales are shared with authorized sales staff in the same branch. Recall does not claim an exclusive lock. Posting or replacing the hold closes its old version atomically, so another counter cannot post that version again.
7. Recall uses today's business date; review the original payment reference/date, due date, warehouse and prices. Final posting revalidates current stock, expiry, minimum prices, customer credit controls and open accounting period. A rejected posting leaves the hold available for correction. Re-holding saves a replacement version; cancellation needs a reason and retains audit history.
8. Post the sale. Open its invoice in Documents, choose Print payment receipt when available, and print using the existing print control. Close the till with a physical count at day end.

## Barcode setup

Open Barcodes. Users with catalogue.write can assign a unique code to a product and a base-unit conversion. SKU scanning works without an assignment, and barcode/SKU collisions are rejected. A serialized product's code must represent one unit. Barcode assignments are permanent in this preview; check them before saving. Reassignment/retirement, barcode image/label generation, camera decoding and GS1 embedded weight/price parsing are not implemented.

## Limits and validation

Held drafts are local to the company database and do not synchronize across computers. They do not reserve stock or create orders. Order-linked and delivery-linked transactions continue through their own workflows. The 200-line limit still applies. Scanner hardware, thermal receipt layouts, interactive native keyboard/printing and clean-machine installer acceptance remain unverified. Automated service/UI tests cover barcode pack entry, tracking selection, editing identical scanned rows separately, held-sale durability, replacement/cancellation, replay and duplicate-post prevention, current price/stock revalidation and a full till/sale/refund/closing flow. Live user data is not used for development verification.
