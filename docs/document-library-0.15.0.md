# Document library — 0.15.0

**Documents → Document library** captures and keeps the original papers behind the business: tender documents, cheques, supplier invoices, receipts, contracts, signed delivery notes, proformas and quotations, certificates and licences, and letters. It works in both the desktop app and the browser host. It replaces the separate "Scan & capture" inbox, and documents captured there are moved into the library automatically.

## Capturing
- **Choose file:** PDF, photos (JPG, PNG, TIFF, BMP, GIF, WebP), Word, Excel, PowerPoint, text or CSV, up to 20 MB each. The file's content must match its extension, so a renamed program file is refused.
- **Scan a page** (desktop): uses the Windows scanner dialog (WIA). For multi-page scanner jobs, save a PDF in the scanner software and choose it.
- **Take a photo** (browser host): opens the camera on a phone or tablet, for example to photograph a cheque at the counter.
- **Text reading (desktop):** reads printed text on the computer (offline) and suggests the document type, reference, party, currency, and the printed date, total and deadline. Every suggestion must be checked.
  - Files with no readable text, such as handwritten cheques or photos, are still stored; enter their details by hand.
  - Optional online AI suggestions (OpenAI) are still available on the desktop. Only the extracted text is sent, never the file, and only after you tick consent each time.
- **Duplicates:** the exact same file cannot be filed twice; the app opens the existing document instead.

## Details kept for each document
- Type, title, reference or number, customer/supplier/institution, document date, amount and currency.
- **Deadline / expiry date:** tender closing date, cheque validity, certificate or licence expiry.
- Tags, notes.
- **Confidential:** only users with `accounting.read` or `users.manage` can see confidential documents or mark them confidential.

**Deadlines and expiry dates** at the top of the library lists documents due within 60 days. Overdue items, up to 30 days late, are shown in red.

## Links and attachments
A document can be linked to:
- a **transaction** (sale, receipt, supplier invoice, payment…);
- a **customer or supplier**;
- a **received cheque**;
- an **equipment serial unit**;
- an **installation**.

Linked documents also appear in an **attached documents** panel on these screens, where a file can be attached directly:

| Screen | Panel |
|---|---|
| Documents (selected transaction) | Attached documents |
| Banking → Received cheques | Cheque images and documents (opens on request) |
| Customers & suppliers (when editing a party) | Documents for this customer / supplier |
| After-sales → Equipment lookup | Documents for this unit |
| After-sales → Installations | Signed sheets, photos and evidence |

## Record-keeping rules
- **Originals are never changed.** Each file is stored with its SHA-256 fingerprint, and the fingerprint is checked every time the file is downloaded or shown. An altered file is refused with a warning, and the Backups & integrity check also reports it.
- **New versions:** upload a revised tender, an addendum or a clearer scan with a reason. Earlier versions stay downloadable.
- **Edits** to details create a new version of the details, kept in the history with who, when and why. A save based on outdated details is refused.
- **Void, not delete:** voiding (needs `documents.reverse`) hides a document from normal lists with a reason. Its files and history remain, and "Include voided" shows it.
- **Links** can be removed with a reason, and the removal is kept.
- **Who can add documents:** anyone who can sell, receive stock, post accounting, edit the catalogue or manage users. Read-only users can view and download non-confidential documents.

## Storage
- Files are stored once by content inside the company database, so they are included in every automatic and manual backup and in restores. A file used by several documents is stored once.
- Storage goes through a replaceable file store, so files can later move to a server folder or to Azure Blob Storage without changing how the library works (see the database plan below).
- Earlier "Scan & capture" originals stay where they were. The library refers to them rather than copying them.

## Database plan: SQL Server Express backup and Azure SQL for several sites
This is not built yet. It is the recommended order of work:

1. **Make the application run on SQL Server** (needed for both goals).
   - The browser host already has a SQL Server switch, but the database migrations and the tamper guards are written for SQLite.
   - Work needed:
     - a separate set of SQL Server migrations;
     - T-SQL versions of the tamper guards;
     - running the full test suite against SQL Server Express (LocalDB) as part of every build.
2. **SQL Server Express as an always-current backup.** Because posted history is append-only, a one-way copy is simple and safe:
   - after each automatic backup, new rows are copied to a SQL Server Express database on the office server;
   - the copy is checked with the same seal-chain fingerprints.

   This gives a live, queryable second copy for reporting and recovery, in addition to the backup files that already go to another drive.
3. **Express limits:** each database is limited to 10 GB, so document files should not be kept inside SQL Server Express. They should go to a server folder through the file store above, keeping only their fingerprints in the database.
4. **Azure SQL for multi-site operation (Phase 5, synchronisation):**
   - each branch keeps working offline on its local database;
   - an outbox and inbox synchronise with a central Azure SQL database when the internet is available;
   - documents go to Azure Blob Storage, where fingerprint-addressed files upload once and verify themselves;
   - every record already carries its branch and device-numbered references, so numbers do not clash between sites.

   Head office then sees all branches together. Offline working stays essential because connectivity can be unreliable.

## Database changes
Migration `DocumentLibrary` adds the tables below, all protected by the database guards and the integrity seal chain. It also brings existing captures into the library.
- `StoredDocuments`
- `StoredDocumentRevisions`
- `StoredFiles`
- `FileBlobs`
- `StoredDocumentLinks`
- `StoredDocumentLinkRemovals`

## Verification
211 tests pass. New tests cover:
- tender filing, search inside the text, the deadline list, versions with reasons, stale edits refused, voiding permissions, and re-filing after voiding;
- a cheque photo with no text linked to its cheque, transaction and customer, with unlinking and relinking;
- file type and size checks;
- confidentiality and permissions;
- tamper detection on download and by the integrity check;
- upgrading a database with earlier captures;
- text suggestions for cheques, tenders, invoices and contracts.

Rendered-screen tests file a tender with suggested details and a link, and attach a file through the attached-documents panel. The browser host was checked visually at desktop and phone widths with a real PDF tender and a cheque photo.

## Limits
- Text is read only by the desktop app (Windows OCR, English print); the browser host stores files without reading their text.
- In-page preview covers PDF, common images and text. Word, Excel and TIFF files are downloaded and opened in their own programs.
- Documents cannot be linked to tenders yet; that comes with the tenders module (0.16.0).
