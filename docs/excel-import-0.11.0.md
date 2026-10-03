# Excel import — 0.11.0

Implements specification **Skill 10 — Excel inventory import & bulk stock onboarding**, the outstanding exit condition of roadmap Phase 2: opening stock can be loaded from Excel and reconciles to Stock Summary by location and tracking mode.

**Inventory → Import from Excel.** Requires `catalogue.write` (products, prices) or `inventory.receive` (counts); stock imports also require `accounting.write` because they post to the ledger.

## Six import types and their templates

| Import | Creates | Permissions |
|---|---|---|
| Product catalogue | New products, optional confidential minimum price and barcode (with units per scan) | catalogue.write (+ pricing.manage for minimum prices) |
| Opening stock | Opening stock documents for quantity-tracked products, by warehouse | inventory.receive + accounting.write |
| Serial inventory | One row per serial | inventory.receive + accounting.write |
| Batch and expiry inventory | Quantities by batch and expiry | inventory.receive + accounting.write |
| Price update | New selling prices and/or new minimum-price versions | catalogue.write (+ pricing.manage) |
| Stock count | Count sessions awaiting approval by a second person (200 lines per session) | inventory.receive |

**Download Excel template** generates a workbook with a Data sheet and an Instructions sheet describing every column. The desktop app saves it to *Documents\Sierra Business*; the browser host downloads it.

## Wizard
1. Choose the import type (only types your permissions allow are offered).
2. Choose the workbook (.xlsx only) and sheet.
3. Match columns. Columns with matching or common names (Item Code, Qty, Cost Price, Location, Lot, Expiry…) are matched automatically.
4. **Check the data**: every row is validated with the same rules as manual entry, with plain explanations for warnings and errors.
5. **Download error workbook**: your original rows plus *Excel row*, *Import status* and *What to fix* columns.
6. Import. Committing is blocked while any row has an error. All rows are re-checked inside the commit transaction, so changes made after checking (for example a serial received meanwhile) stop the import instead of creating conflicts.

## Accounting
Opening stock posts one **Opening stock** document per warehouse, dated the chosen "as at" date (open period, not in the future): **Dr 1200 Inventory / Cr 3200 Opening balance equity** (new standard equity account). Stock value equals the inventory ledger. A wrong opening stock document is corrected by reversing it under Documents, like any other document.

## Safety
- `.xlsx` only, read with the Microsoft Open XML SDK; Excel/Office is not needed. Macro-enabled files are refused; nothing in a file is executed.
- Limits: 10 MB, 10,000 rows, 60 columns, 2,000 characters per cell.
- Formulas: the saved result is used; a formula without a saved result is reported on its row.
- Dates: Excel date cells, dd/mm/yyyy, d/m/yyyy, yyyy-mm-dd, dd-mm-yyyy and "3 Oct 2026" forms. Day comes before month.
- **Retry-safe**: each import has a fingerprint of its normalised content. Committing the same data again, even from a re-saved file, creates nothing and says when it was imported. A repeated commit click is also safe.
- Checked-but-not-imported staging is discarded after a day; committed imports appear in **Import history** and in the audit trail with the file checksum.
- Imported records are sealed by the 0.10.2 integrity chain like all other posted history.

## Validation performed
Duplicate SKU, barcode or serial (within the file and against existing records), unknown product or warehouse, wrong template for the product's tracking mode, inactive products, invalid numbers and dates, missing required fields, zero or negative quantities and costs, expired batches (warning), missing expiry for Batch+Expiry products, minimum price above selling price (warning), surplus counts without a cost, duplicate count identities, and permission for minimum prices.

## Database
Migration `ExcelImports`: staging tables `ImportBatches` and `ImportRows`, standard ledger account 3200 *Opening balance equity*, and the new document kind *OpeningStock*.

## Verification
174 tests pass, including the specification's acceptance criteria: a 1,200-product import in one atomic commit (under a second), duplicate serial rejection, opening stock across warehouses reconciled to the ledger, batch/expiry import with Excel and text dates, malformed, empty, oversized and macro workbooks, retried and re-saved imports creating nothing, and error-workbook generation. A rendered-screen test drives the full wizard (template, upload, mapping, errors, error workbook, fixed upload, import) against the real service, and the browser host was checked with a real file and download.

## Limits
- Products cannot be deleted by import; deactivate them under Product settings.
- Variant and compatibility tracking remain unavailable (specification rule 6 is still open).
- CSV is not accepted; save CSV files as .xlsx.
