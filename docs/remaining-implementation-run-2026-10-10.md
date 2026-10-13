# Remaining implementation run — 10 October 2026

User authorization: implement all areas listed in the next-work response: verified company period splitting; inventory valuation, consignment and pre-invoice rejections; cheque printing, reviewed bank-row vouchers and broader imports; intercompany eliminations; Windows packaging and available acceptance checks.

## Implementation status

- Period cutover, archive protection and continuation creation implemented and tested. Full reference history is retained; current-head standalone cutovers are supported, not retrospective compaction or synced-branch migration.
- Reviewed bank-row vouchers and atomic cheque issuance implemented and tested.
- Cheque books, layout previews, marked test printing and print audit implemented. Physical alignment unverified.
- New-product book costing, alternative valuation reports, owned/supplier consignment and pre-invoice rejections implemented and tested.
- Party, ledger and atomic grouped-voucher imports implemented and tested.
- Balanced group elimination adjustments implemented and tested.
- Windows package, offline capture/OCR smoke check and all 26 release artifact checks passed. Installed-app/native visual acceptance remains unverified.

Physical peripherals, a clean external machine, signing certificate, external fiscal/bank services and live provider credentials are external acceptance dependencies. Report exact checks performed; do not mark them complete without evidence.

The previous group-reporting work passed 21 affected tests. The earlier 355-pass full-suite result predates both this run and group reporting.

The new focused suite passed 25 tests. Final verification on 11 October 2026 passed 380 full Release tests with zero failures and six skipped live database-provider tests. Authenticated HTTP checks passed. The unsigned 0.32.0 installer was published under artifacts/windows/0.32.0 and passed all 26 artifact checks. Execution policy blocked isolated UI-preview startup without a specific reason; native visual and clean-machine acceptance remain unverified. See remaining-features-0.32.0.md for exact workflows, safeguards, evidence and limits.
