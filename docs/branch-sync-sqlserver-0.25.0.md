# Multi-branch sync and SQL Server — 0.25.0

Each branch keeps working on its own computer, online or offline. Head office runs the web server, on SQLite or SQL Server, and collects every branch's records.

## How it works
- **Changes are queued with the work.** Every saved change is written to an outbox in the same save as the change, so nothing is lost or half-sent.
- **The branch syncs every 5 minutes** while the app is open, or straight away with **Administration → Branch sync → Sync now**. Each sync does two things:
  1. *Receive.* The branch takes head office's shared records: products, variants and bundles, price rules, tax categories, document templates and other settings, plus any records head office entered for that branch.
  2. *Send.* It then sends everything made at the branch: sales, receipts, journals, stock movements, payroll, cheques, tenders, service jobs, document-library files and so on.
- **What each branch sees.** A branch never receives another branch's records. Document-library files go only to head office.
- **First sync.** When a branch first connects, records it made before joining are sent once. A new branch should sync before it creates products, so it starts from head office's catalogue.
- **Safe to repeat.** Changes travel in whole saves (a sale never arrives without its lines). A retried send is recognised and ignored, and an interrupted sync carries on from where it stopped.
- **Offline.** Changes stay queued at the branch. **Branch sync** shows how many are waiting, the last successful sync and the last error.

## Conflicts
- **Shared records** (such as a price rule) changed at head office and at a branch from the same version: head office's version is kept everywhere, and both sides list the conflict.
- **Posted history** (invoices, journals, movements…) is never overwritten. If a posted record differs, the existing copy is kept and the difference is listed.
- **Records that cannot be saved** at head office, such as a product code a branch also created separately, are listed with the reason, and the rest of the sync continues.
- **A branch may only send its own records.** Anything addressed to another branch is refused and listed.

Review conflicts under **Branch sync** and mark each one reviewed.

## Setting up
**Head office (web server)**, in `appsettings.json` or as environment variables:
```json
"Trading": { "Branch": "HQ" },
"Sync": { "Role": "Central", "Keys": { "FREETOWN": "<long random key>", "BO": "<another key>" } }
```
- Keys are at least 16 characters; use 32 or more random characters.
- The server must be reached over HTTPS.
- The endpoints are `POST /api/sync/push` and `GET /api/sync/pull`. A branch is identified by the `X-Sierra-Branch` header and its key, sent as `Authorization: Bearer`. Keys are compared in constant time and never appear in addresses or logs.

**Each branch (desktop app):**
1. Under **Administration → Branch sync**, enter head office's `https://` address and the branch key. This needs `users.manage`.
2. Restart Sierra Business.

The branch code in the trading settings must match the code head office has a key for. The connection is stored in `sync-settings.json` in the company's data folder, inside the user's own profile.

A web server can also run as a branch: `"Sync": { "Role": "Branch", "Server": "https://…", "Key": "…" }`.

## SQL Server
Head office can use SQL Server instead of SQLite:
```json
"Database": { "Provider": "SqlServer" },
"ConnectionStrings": { "Business": "Server=…;Database=SierraBusiness;User Id=…;Password=…;Encrypt=True" }
```
1. Run `SierraBusiness.Web --migrate` once to create the database. It uses the SQL Server schema in `SierraBusiness.Infrastructure.SqlServer`.
2. On SQL Server, posted history is protected by a database trigger on each history table. The trigger refuses any `UPDATE` or `DELETE`, even from tools outside the app. The seal chain and the integrity check work as on SQLite.
3. Back up the SQL Server database with your normal SQL Server backups. The app's built-in backups are for the desktop SQLite database.

## Tests
- `SyncTests` uses head office and two branches on separate databases:
  - masters flow down and branch records flow up;
  - branches are isolated from each other;
  - retries are harmless;
  - conflicts on both sides and refused records from another branch;
  - key checks;
  - offline queuing;
  - history from before joining, and attachments;
  - a real HTTP exchange with the branch keys.
- `SyncScreenTests` drives the Branch sync screen.
- `SqlServerTests` runs against a live SQL Server:
  - posting, reversal, vouchers, statements and reports;
  - the database triggers blocking edits and deletes;
  - integrity verification;
  - a SQLite branch syncing into a SQL Server head office.

  The tests run when `SIERRA_SQLSERVER` holds a connection string, and are skipped otherwise. They passed against SQL Server 2022 in development.

277 tests pass with SQL Server available. The Windows package run skips the two SQL Server tests.
