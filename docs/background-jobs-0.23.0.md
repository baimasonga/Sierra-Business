# Background jobs, reminders and system health — 0.23.0

This release implements specification **Skill 25** (background workers, resilience and observability). The system now looks after itself while it runs: it raises reminders, retries work that failed, checks its own records and shows administrators how it is doing.

## Background tasks
A scheduler checks every minute for tasks that are due. It runs inside the desktop app while it is open, and inside the web server.

| Task | Every | What it does |
|---|---|---|
| Reminders | 6 hours | Creates the reminders listed below |
| Fiscal registration | 15 minutes | Sends invoices waiting for fiscal registration whenever the device or service is available |
| Integrity check | 24 hours | Verifies the database guards and seals on all posted history |

**Resilience:**
- **Safe to repeat.** Every task can safely run again; reminders are created once, by subject.
- **Retries.** A failed task is retried after 1, 2, 4, 8… minutes, never more than an hour apart.
- **When a task stops.** After **8 failures in a row**, or at once for a failure retrying cannot fix (such as a configuration fault or a failed integrity check), the task **stops**. Administrators get a reminder, and **Run now** restarts it. Work is never silently dropped.
- **Restarts.** The state of each task (last run, last success, next run, failures, checkpoint) is kept in the database, so a restart carries on where it left off.
- **Logs.** Logs carry the branch, task and attempt. Error messages are cleaned of passwords, tokens and keys before they are stored or shown.
- **New databases and upgrades.** Tasks wait until the database exists and is fully upgraded.

## Reminders
**Overview → Reminders** lists what needs attention, filtered by each user's permissions:
- tenders closing within 3 days;
- maintenance visits due within 7 days;
- customer warranties ending within 30 days (a good time to offer a maintenance contract);
- customers with overdue balances (once a week per customer; needs `accounting.read`);
- approvals waiting more than 4 hours;
- last month's PAYE or NASSIT not yet remitted (needs `accounting.write`);
- stock below its reorder level (once a week per product and warehouse);
- library documents due or expiring within 14 days;
- background tasks that have stopped (for `users.manage`).

Each reminder links to the screen where it is dealt with. **Dismiss** hides it for you only; others still see it.

## System health
**Administration → System health** (needs `audit.read` or `users.manage`) shows:
- **The software:** version, .NET runtime and operating system.
- **The database:** its type and size, the schema version, and any upgrades still pending.
- **Branch and device.**
- **Last automatic backup** (desktop) and the **last integrity check**.
- **Record counts:** documents, products, parties, employees, library documents and users.
- **Every background task:** its interval, last run, last success, next run, status and last message, with **Run now**.
- **The last 50 runs.**

No passwords, keys or connection strings are shown.

The web server's `/health` endpoint now also reports **Degraded** when a task has stopped or has missed three intervals.

## Database
Migration `BackgroundJobsReminders` adds `JobStates`, `JobRuns`, `Reminders` and `ReminderDismissals`.

## Verification
260 tests pass on Linux.
The new tests cover:
- **Retries.** A task failing twice is retried after 1 and then 2 minutes, then recovers and saves its checkpoint.
- **Restarts.** A restarted runner keeps the saved schedule.
- **Stopping.** A permanent failure stops a task at once; eight transient failures stop another. Both raise administrator reminders that clerks do not see, and the stored message is masked.
- **Health.** Health reports stopped and late tasks.
- **Reminders.** Reminders for a closing tender, an overdue customer and low stock are created once, even when the task runs twice. Dismissing is per user, and the overdue reminder is hidden from a clerk.
- **Fiscal registration.** A pending fiscal invoice is retried while the device is offline and registered once it is back.
- **Screen test.** Run tasks from System health, see the reminder, then dismiss it.

## Limits
- **Desktop.** Tasks run only while the desktop app is open; there is no separate Windows service yet.
- **Delivery.** Reminders appear in the app only. Email and SMS delivery are not part of this release.
