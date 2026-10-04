# Acceptance checklists (need the owner)

These cannot be done from the build machine. Tick each one before distributing to customers.

## 1. Code-signing certificate
- [ ] Buy an OV or EV code-signing certificate in the company's legal name. EV builds SmartScreen reputation immediately.
- [ ] Install it in the certificate store of the build PC (or hardware token). Set `SIERRA_SIGN_THUMBPRINT` for the build account.
- [ ] Run `artifacts\windows\build-<version>.cmd`. The log shows `Signed: SierraBusiness.Maui.exe` and `Signed: SierraBusiness-Setup-x64.exe` with your company name.
- [ ] On another PC, right-click the installer → Properties → Digital Signatures. The signature is valid and timestamped.
- [ ] Download the installer through a browser. SmartScreen shows the publisher name, not "Unknown publisher".
- [ ] Publish the update feed (`latest.json`) on your https site with the installer's SHA-256.

## 2. Real hardware (at a counter)
- [ ] **Receipt printer** (80 mm thermal): print a cash sale, a credit invoice and a payslip. Check the layout, logo, TIN and totals.
- [ ] **A4 printer**: invoice, delivery note, quotation, statement of account, financial statements.
- [ ] **Barcode scanner** (USB keyboard mode): scan a product, a pack barcode and a serial-numbered unit on New sale.
- [ ] **Cash drawer** (opened by the receipt printer): opens on cash sales only. Till close matches the drawer count.
- [ ] **Document scanner or phone camera**: capture a supplier invoice, and check the offline text reading and the library search.
- [ ] **Fiscal device** (if the NRA requires one for you): register an invoice, then work offline and check the automatic retry.
- [ ] **Speed**: post 20 sales in a row on the counter PC. Each one should finish in well under a second. If not, exclude the data folder from antivirus real-time scanning and try again.
- [ ] **Power cut**: switch off during a sale. On restart, nothing is half-posted and **Backups & integrity → Verify** passes.

## 3. Clean Windows machine
- [ ] A fresh Windows 10/11 x64 PC or virtual machine with no developer tools. Install with no internet (the WebView2 runtime is bundled).
- [ ] First start: create the owner account, set the company details, branch and tax settings.
- [ ] Post a receipt, a sale, a return and a payroll. Restart and check that everything is still there.
- [ ] Install the next version over it. A `before-upgrade` backup appears and the data is intact.
- [ ] Roll back as described in [production readiness](production-readiness-0.26.0.md#upgrade-and-rollback).
- [ ] Uninstall. The program is removed and the data folder stays.
- [ ] **Branch sync**: connect two clean machines to a head-office server (https). Sync, then disconnect the network for an hour of trading, reconnect, and check head office's totals.
