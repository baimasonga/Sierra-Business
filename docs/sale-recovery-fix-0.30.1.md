# Sale recovery cleanup - 0.30.1

The reported New sale failure was reproduced as a missing-directory exception: clearing an empty recovery copy called File.Delete before the company draft directory existed. Initial recovery setup then failed, and navigation could throw an unhandled exception that stopped the workspace.

Desktop recovery now checks whether the recovery file exists before deleting it. Workspace navigation catches genuine cleanup failures, displays an error and leaves the current sale open for editing. Recovery exceptions are sent to the configured application logger; this does not introduce a persistent log-file provider.

Regression coverage checks repeated cleanup for a fresh company without creating a drafts directory, and simulated cleanup failure followed by continued editing, cancellation and successful navigation. The two focused tests passed. The fixed-source Release suite passed 307 tests with zero failures and six skips (313 total, 17.0360 minutes). OCR smoke verification passed with a 94% score. Windows self-contained publishing and Inno Setup compilation succeeded.

The unsigned installer is artifacts/windows/0.30.1/RokelaBusinessAdmin-Setup-x64.exe (289,984,459 bytes, ProductVersion 0.30.1). Independently verified SHA-256: 003D2D10C9989CF56C1C595D7C831F0032EC9B618407617B64D29FE648B1AF19. Test report: artifacts/windows/0.30.1/tests/tests.trx. Packaging logs are retained beside the installer.

The release was built from artifacts/windows/source-0.30.1, a copy of 411 source files with hashes in source-manifest.json. It includes the deposits/advances increment and the financial ratios increment present when the snapshot was taken. All manifest source files still matched the workspace after compilation. Real-device acceptance and clean-machine installation remain separate from automated build verification.
