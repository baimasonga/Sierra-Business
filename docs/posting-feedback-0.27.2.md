# Invoice posting feedback

7 October 2026: posting validation errors now appear beside the Post document
button as well as at the top of the workspace. Credit settlement explains that
the Customer field must name a registered customer; a hold label names a draft.
Empty documents show instructions to select a product and add a line.

All six WorkspaceHeaderTests passed. The new regression adds a stocked product,
rejects a Walk-in credit invoice with visible nearby feedback, then changes the
same draft to Cash and confirms successful posting in the document register.
This is a shared-component/service test, not a native Windows mouse-click test.

Installer `artifacts/windows/0.27.2/SierraBusiness-Setup-x64.exe` compiled
successfully: 285,034,590 bytes, product version 0.27.2. The packaging run passed
289 tests with zero failures and six live database-provider skips. Offline OCR
checks passed at 94%; native publish and Inno Setup compilation succeeded.
Build logs are in `artifacts/windows/0.27.2/package-output.log` and
`package-errors.log`. The unsigned installer has not been installed or tested
through native Windows clicks in this run.
