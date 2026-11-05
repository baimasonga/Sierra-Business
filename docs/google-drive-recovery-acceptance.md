# Google Drive backup recovery: acceptance procedure

Use a disposable company and a test Google account for the live check. Local import and restore can be exercised without any Google credentials.

1. In the disposable company, receive ten units of a test product and sell one. Create a manual backup at nine units.
2. Under Backups & integrity > Encrypted Google Drive backups, select the administrator-provided Google desktop credentials JSON in Rokela. Save the recovery key separately, confirm that it is saved, sign in using the browser and enable uploads. Keep credentials and recovery keys out of test logs.
3. Upload the verified nine-unit backup, download its .rokela-backup file from Drive, then sell one more unit in the test company. Stock should now be eight.
4. Use “Recover a downloaded encrypted backup” to import the downloaded file. Check that a wrong key is rejected; import with the correct key. Current stock should remain eight until a restore is explicitly requested.
5. Restore the imported backup with the RESTORE confirmation. Sign in again: stock should be nine, protected history should verify and a pre-restore safety copy should exist. Sell one unit to confirm trading still works.
6. Disconnect uploads and remove only the disposable test artifacts from Drive after recording the outcome.

Automated coverage: RecordsSafetyTests.EncryptedCloudFileImportsForReviewBeforeRestoringTrade exercises steps 1, 4 and 5 using the actual encrypted file format and desktop services. CloudBackupTests also exercises damaged files and a simulated Drive transport. Neither test certifies Google browser authorization, resumable upload or download over the live network.
