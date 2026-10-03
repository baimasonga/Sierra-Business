# Batch stock counts — 0.7.0

Warehouse controls now support physical counts for quantity, batch and batch-with-expiry products. Counts adjust only the selected warehouse and exact batch/expiry identity, retain an audit record, and post a balanced inventory adjustment journal.

## Counting a batch

1. Open Warehouses and stock controls. Choose the product and warehouse under Physical stock count.
2. Select a recorded batch, checking its expiry date. For newly discovered stock, enter the batch and expiry manually. Expiry is required for expiry-controlled products; other batch products must use the same optional expiry as the recorded lot.
3. Check the displayed book quantity and enter the physical count in base units. Changing product, warehouse or batch resets the count and surplus cost for review.
4. For a surplus, enter its cost per base unit. Shortages use that lot's existing average book cost; counting to zero removes its full remaining value.
5. Enter a reason, review the quantity adjustment and post. If stock has changed, refresh balances and recount. Release reservations before counting below reserved stock.

Expired stock may be counted or written off, but sale validation still blocks expired goods. Counts do not extend expiry dates. Correct an incorrectly posted count through the existing document reversal flow, subject to stock and reservation interlocks.

Both inventory receiving and document reversal permissions are required. Earlier quantity-count operation payloads retain their serialization, so retries remain compatible. This release needs no database schema migration.

## Limits

Serial counts, automatic FEFO selection, multi-line count sheets, stock freezing during counting and barcode label printing are not included. Stale-count validation checks the current quantity; it does not detect intervening movements that leave the same quantity. Native UI and clean-machine acceptance remain outstanding.

## Validation

Targeted service tests cover lot/expiry/warehouse isolation, valuation, replay, reversal, stale quantities, reservation protection, expired stock and legacy payload compatibility. A component-handler test covers batch selection and input reset. Full-suite and installer results are recorded in verification.md after packaging.
