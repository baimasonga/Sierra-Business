# Records safety — 0.10.2

This release protects existing records before more modules are added. It follows the specification's global rules 9 (no silent erasure of history) and 15 (resilience to power and hardware failure), and Phase 7 backup/restore.

## What changed

**Backups & integrity screen** (Administration). Visible to users with `audit.read` or `backups.create`.

### Tamper-proof history
- **Database guards.** Posted documents, stock movements, journals, payments, cheques, tills, orders, returns, payroll, periods, tax rates, audit records and the seal chain itself cannot be updated or deleted, even with a database tool such as DB Browser for SQLite. Guards are lifted only while the application applies an upgrade and are reinstated immediately afterwards.
- **Seal chain.** Every save that adds posted history also stores a seal containing a fingerprint of each new row and the previous seal's hash. **Check records now** recomputes every fingerprint and reports rows that were changed, deleted or added outside the application, broken links in the chain, and missing guards.
- **Chain fingerprint.** The screen and each backup show the first 16 characters of the latest seal. Write it in the daily cash book or till report: an attacker with full file access could rebuild the chain, but not one that matches fingerprints already recorded on paper.
- On the first start after upgrading, existing history is sealed as a baseline ("Protection started on …"). Rows that existed before that moment are protected from then on; earlier changes cannot be detected retroactively.

### Automatic and off-machine backups
- On by default: a verified backup every 24 hours while the application is open (checked every 30 minutes and at start-up). No automatic backup is made before the owner account exists.
- **Copy folder**: an optional second location, ideally another drive, a USB disk or a synced cloud folder. Each copy is checked byte-for-byte against the original. If the drive is disconnected, the local backup is still kept and the screen says the copy failed.
- Only automatic backups are trimmed (default: keep 14). Manual, before-upgrade and before-restore backups are never deleted automatically.
- Each backup has a `.manifest.json` with its SHA-256 checksum, schema version and chain fingerprint.
- Settings are stored in `backup-settings.json` in the company data folder.

### Restore
- Owner-level only: requires both `backups.create` and `users.manage`.
- Choose a backup, type `RESTORE`, and confirm. Before anything is replaced the backup is checked for: checksum match, SQLite integrity, a schema no newer than this version, at least one user account, and the same branch.
- A **before-restore** safety backup of the current records is always made first, so a restore can be undone.
- After restoring, older backups are upgraded automatically, guards are reinstated, the integrity check runs, and everyone must sign in again.

### Stock count approval
- When another active user can approve counts (holds `documents.reverse` and `inventory.receive`), the person who prepared a count cannot approve it. In a shop with a single approver, self-approval is still allowed and is labelled "self-approved" in the decision reason. Rejection is not restricted.

## Upgrade notes
- New migration `IntegritySeals` (additive). A before-upgrade backup is taken automatically, as before.
- The first start after upgrading reads all history once to create the baseline seal; very large databases may take longer to open that one time.
- Browser host: run `--migrate` as before; it now uses the same guarded upgrade. Backups and restore remain desktop-only.

## Limits
- The database file is not encrypted. Guards and seals make tampering visible; they do not stop someone with file access from copying or reading data.
- Verification reads all history into memory; fine for a single shop, to be revisited with reporting projections.
- The copy folder must be typed as a full path (for example `E:\SierraBackups`).

## Verification
157 integration and screen tests pass on .NET 10, including direct-SQL tampering attempts (edit, delete, insert, chain rewrite), guard reinstatement after upgrade, automatic backup scheduling and trimming, copy verification and disconnected drives, restore with safety backup and sign-out, rejection of damaged, newer-version and unauthorised restores, count approval separation, and rendered screen interaction.
