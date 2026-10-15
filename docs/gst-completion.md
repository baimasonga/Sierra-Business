# GST completion: stock recovery after issue, standard cost and return corrections

Source work on 15 October 2026. Completes the software parts of backlog item 1 in [the remaining backlog](remaining-backlog-implementation-2026-10-11.md). Independent acceptance by a tax practitioner or the NRA is still outstanding; nothing here files with the NRA.

## Input GST on stock that has already moved

Before, a stock claim was refused once any of the receipt's goods had been sold, issued, transferred or written down. **Company & tax → Input tax** now accepts the claim and allocates the allowable GST:

- **Goods still held** (up to the quantity received), including goods transferred to another warehouse — the product's carrying value is reduced by the GST per unit: first on the units still in the receipt's own warehouse/batch/serial lines, then on the product's other stock. No stock line drops below zero.
- **Goods already sold or issued** — the rest reduces **5000 Cost of sales**.
- **Standard-cost items** — their stock stays at standard value. The GST share reduces **5100 Operating expenses**, where the receipt posted its purchase-price variance.

Example: 10 units received at 115 each (100 + 15 GST), 4 sold, all 150 GST allowable → Dr recovery asset 150, Cr Inventory 90, Cr Cost of sales 60.

The receipt must still be a direct, invoiced goods receipt whose gross equals the reviewed net plus GST. Claims are refused while the receipt has an active purchase return. Reversing the claim reverses every part of it.

## Corrections to recorded GST returns

A recorded monthly return stays immutable. When an amended return or NRA assessment changes a filed month, use **Company & tax → Monthly GST record → Corrections to recorded returns** (needs `accounting.write` and `users.manage`):

| Field | Meaning |
|---|---|
| Output GST under-declared | Extra output GST for that month (negative if over-declared) |
| Input GST newly allowable | Extra input credit (negative if over-claimed) |
| Book the tax difference to | Non-control income or expense ledger that absorbs the difference in the books |
| Brought-forward credit to use | Part of the extra tax settled from the GST carry-forward asset recorded on that return (the only asset a correction may use or credit) |
| Penalties and interest | Paid with the correction and charged to their own expense ledger |

The correction is dated **today**, so locked periods are never reopened. Extra tax is paid from a local cash/bank ledger (less any brought-forward credit used); a net reduction becomes carry-forward credit. Each correction needs a reference that is unique for its return, and corrections cannot be reversed. Recorded corrections are listed in the same section.

## Still outstanding

- Submitting returns to the NRA, and independent acceptance of these treatments.
- Retrospective tax-period partitioning (see the backlog).
