# Sync safety and sale recovery — 0.29.0

## Compatibility

HTTP sync now authenticates a compatibility endpoint before every exchange.
Protocol 1 and replicated-entity schema 1 are supported. Pull/push requests must
carry matching protocol/schema headers; missing or unsupported versions receive
HTTP 426 before data is read or written. An older server without the endpoint is
refused by the client. Local changes stay queued, and trading continues offline.

| Branch | Head office | Result |
|---|---|---|
| 0.29.0 (protocol 1/schema 1) | 0.29.0 | Supported |
| 0.28.0 or earlier, without handshake | 0.29.0 | Upgrade required |
| 0.29.0 | 0.28.0 or earlier, without endpoint | Upgrade required |
| Future protocol/schema | 0.29.0 | Refused until explicitly supported |

Upgrade head office and branches together before resuming sync. There is no
legacy bypass. Protocol/schema numbers describe the replicated wire contract,
not EF's per-row concurrency versions. Increment them when wire/model changes
are incompatible and update the matrix and cross-version tests.

## Saved secrets

Desktop sync keys use Windows DPAPI CurrentUser encryption. Existing plaintext
keys migrate on load through an atomic file replacement. Re-enter the branch key
after moving settings to another Windows account/machine or if decryption fails;
the Branch sync screen explains this and leaves business records intact. Clearing
the server clears its key. Ordinary raw settings corruption also displays a
re-entry warning. This does not replace central secret-store deployment work.

## Sale drafts

Windows saves accepted field changes and lines in an encrypted recovery copy,
scoped to the company, branch and signed-in user. Copies are atomically replaced
after flushing the temporary file. Recovery is offered explicitly at sign-in.
Dates, settlement, lines, tracking details and the editor entry are preserved.
Review recovered stock/prices/customer/date before posting; posting validation
still runs. Canceling discard keeps the draft; confirming discard clears it.

Posting and holding operation IDs are preserved. A committed posting or hold
suppresses its recovery copy, covering a crash after commit but before cleanup.
Holding remains available independently through Held sales. Typed characters
that have not fired a field change are not saved. This release covers sales,
not goods receipts, tenders, quotations or other unfinished forms.

## Verification

Twelve targeted sync/security/recovery tests passed, then 15 workspace/recovery
checks passed after adding the hold boundary. These include actual Windows DPAPI,
plaintext migration, corrupted-key repair, signed-out access refusal, post/hold
commit suppression, protocol/schema rejection and recovery/discard UI actions.
Native Windows clicks and physical power interruption remain unverified.

## Installer

Built `artifacts/windows/0.29.0/RokelaBusinessAdmin-Setup-x64.exe`, 289,492,896
bytes, product version 0.29.0. Full release run: 295 passed, zero failures, six
live-provider skips (301 total). Offline OCR passed at 94%, native publish and
Inno Setup compilation succeeded. Installer SHA-256:
`90FFAFA1F67E6690A81B51A2419DC2BD89EBB12B8C0AB169739FBBD161077390`.
Build logs are in the release directory. The installer is unsigned and was not
installed during this verification run.
