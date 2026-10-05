# Head office on Supabase (PostgreSQL) — 0.27.0

Branch computers keep their own SQLite database and work offline. Head office can now keep its database in **Supabase** (managed PostgreSQL) as well as SQLite or SQL Server. Branches never connect to Supabase directly: they sync with the Sierra Business head-office server, and only that server talks to Supabase. Approvals, period locks, retries without duplicates, tamper seals and branch isolation therefore all still apply.

## What was added
- **PostgreSQL support:**
  - a PostgreSQL schema (`SierraBusiness.Infrastructure.Postgres`, Npgsql 10);
  - `Database:Provider` set to `Supabase` (or `Postgres` / `PostgreSQL`).
- **Tables kept away from Supabase's public API.** Every table, including the migration history, lives in the `sierra` schema. Nothing is created in `public`, which Supabase publishes through its automatic REST API, so the project's API keys cannot read the business tables.
- **History guards on PostgreSQL:**
  - one trigger per history table refuses any `UPDATE` or `DELETE` of posted records, whoever runs it, including from the Supabase SQL editor;
  - the trigger function has a fixed `search_path`.
- **Time precision.**
  - **Seals.** PostgreSQL keeps times to the microsecond, so seal times and record times are rounded to the microsecond before the seal is computed, and verification passes after the round trip.
  - **Sync.** Rows arriving by sync are compared the way PostgreSQL stores them, so a re-sent record never shows as a false conflict.
- **Index filters** are written for each database's own syntax.

## Tested
### Review fixes — 2026-10-05

- The Windows launcher now builds its connection string with `DbConnectionStringBuilder` through `scripts/head-office-connection.ps1`, preserving passwords containing semicolons, quotes, whitespace and Unicode. This helper runs in Windows PowerShell 5.1 without loading the application's .NET 10 assemblies.
- Timestamp normalization uses UTC ticks, truncates to microseconds and always emits offset zero. It preserves the instant for positive/negative offsets, including input already at microsecond precision.
- Current-session validation: 8 database-independent PostgreSQL compatibility tests passed, including a real Windows PowerShell invocation with seven synthetic password cases; 13 existing sync/integrity tests passed. The PostgreSQL model matches its migration snapshot.
- Four live PostgreSQL tests, including the new timestamp round-trip/seal regression, require `SIERRA_POSTGRES` and were skipped in this session. The earlier live-database results below have not been independently rerun for these fixes. No deployed database was changed and no installer was rebuilt for this patch.

### Earlier implementation verification

- The full suite passes against SQLite, SQL Server 2022 and PostgreSQL 17 together (283 tests).
- `PostgresTests` cover:
  - posting, reversal, vouchers, statements and reports;
  - triggers blocking edits and deletes;
  - integrity verification and restart;
  - no tables in `public`;
  - a SQLite branch syncing in;
  - a branch with existing history joining, then the whole batch being re-sent without a duplicate or a false conflict.
- The packaged head-office server ran against PostgreSQL:
  - `--migrate` created 114 tables in `sierra`, none in `public`, and 86 guard triggers;
  - `--create-owner` worked;
  - `/health` was healthy;
  - a branch with its key pulled the shared records, and a wrong key got HTTP 401.
- **The trial project.** On your Supabase account, the free project `sierra-business-trial` (eu-west-3, PostgreSQL 17) was created. A probe confirmed the `sierra` schema, identity sequences, filtered unique indexes and guard triggers work there. Supabase's security advisor reports no issues.
  - Leftover probe objects: two tables, `sierra."ProbeDocuments"` and `sierra."ProbeChanges"`, which do not affect the app. Delete them from the Supabase dashboard if you like.

## Trying it (free plan)
1. In the Supabase dashboard, open **sierra-business-trial** → **Project Settings → Database** and set a database password you will remember.
2. Click **Connect**, choose **Session pooler**, and note the host (`aws-…pooler.supabase.com`) and the user (`postgres.wmlubgelbkqgqqgbnfwb`).
   - Use the session pooler, not "Direct connection", because direct connections on the free plan need IPv6.
   - Do not use the transaction pooler (port 6543).
3. On this PC, double-click `scripts\head-office-supabase.cmd`. On the first run it asks for:
   - the host;
   - the user;
   - the password, which is stored only in this Windows account's protected storage (DPAPI);
   - a head-office owner account.

   It then:
   - creates the tables;
   - shows each branch's sync key;
   - starts the head office on `http://localhost:5180`.

   Add branches with `-Branches FREETOWN,BO`. Use `-Reset` to re-enter the connection.
4. In the desktop app, under **Administration → Branch sync**:
   - enter `http://localhost:5180` (a branch on the same PC) and the key for its branch code;
   - restart the app;
   - press **Sync now**.

   Branches on other computers need an https address in front of the head-office server.

**Free plan limits.** Free projects pause after about a week of low activity; the dashboard resumes them. A database is limited to 500 MB.

## Moving to Pro
- Upgrade the same organisation to Pro in the Supabase dashboard. The project, connection details and data stay the same, and nothing changes in Sierra Business.
- On Pro, projects are never paused, and daily backups are included. Point-in-time recovery is an add-on and is recommended for accounting data.
- For production, put the head-office server on an always-on host with https, and use `SSL Mode=VerifyFull` with Supabase's CA certificate.

## Configuration reference
```json
"Database": { "Provider": "Supabase" },
"ConnectionStrings": { "Business": "Host=aws-…pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=…;SSL Mode=Require" },
"Trading": { "Branch": "HQ" },
"Sync": { "Role": "Central", "Keys": { "FREETOWN": "<key>" } }
```
