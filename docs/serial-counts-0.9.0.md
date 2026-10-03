# Serial stock counts — 0.9.0

Physical stock counts now support serialized products. Select the product and warehouse, then select an on-hand serial or enter a previously unrecorded serial. Choose Absent (0) or Present (1), enter a reason and review the adjustment before posting. A new serial requires explicit unit cost; removing a missing unit writes off its entire book value through the existing balanced adjustment journal.

Serials are normalized to uppercase. A surplus cannot reuse any serial already in movement history, including sold units, another product or another warehouse. Use transfers, returns or document reversals for previously recorded units. Missing serials with active reservations cannot be removed until the reservation is released. Counts reject stale book quantities and fractional/multiple quantities. Repeated delivery of the same operation returns its original result.

A mistaken shortage can be reversed through Documents, provided that the serial is not already back in stock. Reversing a surplus remains subject to later stock activity and reservation interlocks. Both inventory receiving and document reversal permissions are required. No database migration is needed; absent serial fields preserve older count payloads and operation fingerprints.

This remains a one-serial-at-a-time workflow. Bulk scanner count sheets, discrepancy approval, native scanner acceptance and clean-machine upgrade testing remain outstanding. Verify the physical serial label before posting. The installer is an unsigned development preview.

Validation results and installer checksum are recorded in verification.md.
