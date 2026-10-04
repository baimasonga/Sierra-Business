# Customers, statements, variants and bundles — 0.22.0

This release completes specification **Skill 19** (CRM, customer credit and collections) and **Skill 18** (furniture, stationery and general merchandise).

## Customers and suppliers (Skill 19)
Under **Sales & purchases → Customers & suppliers**:
- **Organisation type.** Individual, Business, Government, NGO, Hospital / clinic, School or Other, or any other type you type in. The list can be filtered by type.
- **TIN.** The customer's TIN now prints on sales invoices, and the supplier's TIN on goods receipts.
- **Named contacts.** Up to 12 per institution, each with a role (procurement, finance, technical / biomedical, management, stores), phone and email.
- **Notes**, for example payment habits.

**Statements of account** (needs `accounting.read`):
- **What it shows.** Opening balance, every invoice, payment, credit note and adjustment in the period with a running balance, the closing balance, an ageing summary (not yet due, 1–30, 31–60, 61–90 and over 90 days) and any unapplied credit.
- **Where the figures come from.** They come from the ledger (receivables 1100, or payables 2000 for suppliers), not from a stored balance. Receipts and payments posted as vouchers are included through their allocations to the party's invoices.
- **Output.** The statement prints on the letterhead with the bank details from the document template, and **Export to Excel** gives the statement and its ageing.
- **Credit limits.** Credit-limit warnings, blocks and approved overrides work as in 0.19.0.

## Product variants (Skill 18)
**Inventory → Variants & bundles → Variants**:
- **Creating a group.** Enter a base SKU, name, price, optional minimum price, and up to three attributes with their values (for example Colour: Black, Brown; Size: Standard, XL). Every combination becomes its own product, such as `CHR-EXE-BLA-XL`, "Executive chair – Black, XL".
- **Separate stock.** Each variant has its own stock, barcode, price, reorder level and history, so selling the black chair never touches the brown one.
- **Adding a variant later.** A new colour can be added at any time. Duplicate combinations are refused.
- **Assembly and delivery.** These can be flagged on the group.
- **Limits.** Up to 200 variants per group.

## Bundle offers (Skill 18)
**Inventory → Variants & bundles → Bundle offers** (needs `pricing.manage`):
- **Package price.** A bundle is 2 to 20 quantity-tracked products sold together at one price, for example a desk and chair for NLe 2,500 instead of 3,000. It can be active, inactive or valid until a date.
- **Selling.** **Add bundle** on New sale adds the products with the bundle price spread over them by list value, and the lines add up to the bundle price exactly. Each product's stock moves as usual. A bundle is not an assembly and creates no manufacturing record.
- **Price checks.** Because the bundle price has already been approved by someone with pricing rights, bundle lines skip the per-product minimum-price check and the discount approval. Below-cost approval still applies.
- **Tampering.** Changed prices, missing products or part bundles are refused when the sale is posted.

## Database
Migration `CustomersVariantsBundles`:
- **New party fields:** organisation type, TIN, contacts and notes.
- **New tables:** `VariantGroups`, `VariantMembers` and `Bundles`.

## Verification
254 tests pass on Linux.
The new tests cover:
- **Statements.** An institution with type, TIN and three contacts; contacts are kept when an older screen saves. The statement shows the opening balance, a partial payment, the running and closing balances, ageing that agrees with the balance, a two-sheet export, and permissions.
- **Variants.** Two colours by two sizes; selling black leaves brown untouched; a variant can be added, and duplicates and wrong attributes are refused.
- **Bundles.** Three bundles sold at the package price with the recommended controls on and minimum prices above the spread prices, with no approval needed. Tampered prices, part bundles and inactive bundles are refused.
- **Screen test.** A printed statement, and creating variants from the screen.

## Limits
- **Statement delivery.** Statements are printed or exported; emailing them is not part of this release.
- **Bundle contents.** Bundles hold quantity-tracked products only, so serial and batch items are still chosen individually at sale.
