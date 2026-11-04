# Closed-app scheduled backup on Windows

Verified on 4 November 2026 using the Windows Task Scheduler service and Windows PowerShell 5.1. The registration helper now computes the same task-name hash using methods supported by both Windows PowerShell and PowerShell 7.

Build the web host and backup worker in Release, then run scripts/accept-scheduled-backup.ps1 as the signed-in Windows user. It creates a unique disposable company under .tools, initializes a test owner, registers a limited-user task, starts it on demand, verifies the completed backup against its manifest and unregisters that task. It never selects a live company. The worker exited 0 and the 1,687,552-byte backup matched its SHA-256. A separate read-only SQLite check found a valid database, no foreign-key violations and the test owner present. No test task remained. A migrated company without an owner correctly produced no backup.

This verifies local closed-app backup in a signed-in session. It does not establish Google Drive recovery, scheduled execution after logoff or a full application restore.
