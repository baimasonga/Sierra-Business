# Earliest-expiry batch suggestions — 0.8.0

Counter sales can now select available batches in earliest-expiry order. Suggestions are read-only stock planning: they add lines to the local draft, do not reserve stock, and do not post a sale. Final posting still checks stock, reservations, expiry, confidential minimum prices and the accounting period.

## Counter workflow

1. Open New sale, choose the warehouse and business date, then select a batch-controlled product.
2. Enter the requested quantity in base units and set Base units per entered unit to 1. Enter the agreed base-unit price, or leave zero to use the catalogue price. Pack scanning remains available through manual tracking entry; automatic suggestions do not divide pack prices or round pack quantities.
3. Leave serial, batch and expiry input fields blank. Choose Add earliest-expiring batches.
4. Review each added line's batch, expiry, quantity and price. Multiple lots produce separate lines. Remove or adjust lines as needed, then post or hold the sale using the existing controls.

The suggestion service reads current stock for this branch and warehouse. It subtracts active reservations and quantities already allocated to the same batch/expiry in this draft, including converted pack quantities. It skips lots expired before the later of today and the selected sale date. Lots expiring on that date remain eligible. Undated lots are last for ordinary batch products; expiry-controlled products require a date. Equal expiry dates use batch-code order for repeatable selection.

If there is insufficient eligible stock or adding suggestions would exceed 200 document lines, no suggested lines are added. Existing manual tracking inputs are preserved and must be cleared deliberately before requesting suggestions. The service requires business.read and sales.create permissions. Its authenticated API is POST /api/v1/inventory/batch-suggestions and requires the existing antiforgery token.

## Limits

This is counter-sale assistance, not enforced FEFO dispatch. Manual lot selection remains available. Suggestions do not claim reservations or guarantee stock until posting. Order-specific reservations and delivery allocation continue through their own workflows. They do not synchronize between computers. Staff must verify physical batch labels; native keyboard/scanner and clean-machine acceptance remain outstanding. No database migration is needed.

## Validation

Tests cover earliest-expiry and stable tie ordering, undated batches, fractional quantities, expired/reserved/draft stock, warehouse isolation, future sale dates, permission/branch denial, invalid inputs and stock revalidation at posting. Component-handler tests cover draft allocation conversion, agreed-price preservation, visible review data and failed requests retaining inputs. All 119 release tests and the authenticated HTTP suggestion-to-sale workflow pass. Offline OCR also passes. Full packaging results are in verification.md.
