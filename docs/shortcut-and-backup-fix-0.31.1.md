# Windows corrections — 0.31.1

## Changes

- F3 no longer intentionally falls through to WebView's Find on page action while navigating to Receive stock. Shortcut configuration is passed to JavaScript as one array argument, rather than separate arguments through the `params object[]` overload. The workspace regression checks assert that exact wire shape.
- Desktop store disposal now cancels and awaits its automatic-backup loop and drains the backup gate before releasing the company lock and disposing resources. The first full packaging attempt exposed a backup file still in use during test cleanup. The corrected full run passed that scenario. No database schema, posted amount or valuation changed.

## Verification — 9 October 2026

- Corrected full Release suite: 318 passed, zero failed, six live database-provider checks skipped; 324 total.
- Focused backup/restore suite: eight passed. Workspace regression suite: 11 passed.
- Offline capture/OCR smoke passed, OCR score 94%.
- Native Windows publish and Inno Setup compilation succeeded.
- All 26 release-artifact checks passed, including source-manifest integrity, packaged assets, version and checksum.
- Installer: `artifacts/windows/0.31.1/RokelaBusinessAdmin-Setup-x64.exe`, 290,023,097 bytes.
- SHA256: `9FDCFE1752A8139F1A43E85CED8F2915F57ECD2F22DF5F982CCA43AA63661A5B`.
- Installer is unsigned. The current installed 0.31.0 app was not replaced, and native F3 revalidation remains pending installation of this version.

The successful build used the immutable 426-file `artifacts/windows/source-0.31.1` snapshot. The failed first attempt and its logs were retained separately; its snapshot is `source-0.31.1-failed`. No installer was emitted from that failed run. Successful TRX, logs, checksums and `release-verification.json` are in the 0.31.1 release folder.

## Next acceptance

Close the installed application, install 0.31.1, and reopen it. Verify F2 opens New sale and F3 opens Receive stock without Find on page. Then perform the posting/restart/printing checklist in a separate acceptance company. External integrations remain deferred; physical printer, clean-machine and two-device acceptance are still pending.
