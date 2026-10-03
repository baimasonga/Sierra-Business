# Tenders and document previews — 0.16.0

Implements specification **Skill 16** (quotations, tenders and institutional procurement sales) and redesigns the document library around previews.

## Tenders
**Sales & purchases → Tenders.**
- Anyone who can sign in can view tenders and bid prices.
- Supplier costs and margins are shown only to users with `sales.create`, `pricing.manage` or `accounting.read`.
- Registering, costing, submitting and recording outcomes need `sales.create`. Approving a price needs `pricing.manage`.

### Register
Enter the tender reference as printed, the institution, title, closing date and time, responsible officer, submission method and notes. The same reference cannot be registered twice for the same institution.

The register lists open tenders first, by closing date, with a countdown ("Closes in 5 days"; red when overdue, amber within a week). Finished tenders show their final bid total.

### Cost the bid
Each line has:
- a catalogue product (optional), description, quantity and unit;
- the supplier's unit cost, its currency and exchange rate (filled from the latest rate);
- a landed-cost percentage (freight, duty, clearing) and a margin percentage.

How the price is worked out:
- **NLe cost** = supplier cost × rate × (1 + landed %).
- **Suggested price** = NLe cost × (1 + margin %). Click it to use it, or type your own bid price.
- A line priced below cost is highlighted, and the running subtotal, cost and overall margin update as you type.
- **Alternative offers** (for example a cheaper model) are marked "alternative to line N". They appear separately on the price schedule and are not added to the total.
- Sales tax at the configured rate is added when the version is saved.

### Versions, approval and submission
- **Versions:** every save creates a new bid version; nothing is overwritten. Each version after the first needs a reason, and a save based on an outdated version is refused.
- **Approval:**
  - Someone with pricing authority approves the latest version.
  - If another pricing approver exists, you cannot approve your own bid. If none exists, the approval is marked "self-approved" in the history.
  - Changing the bid after approval needs a new approval.
- **Submission:**
  - Record the submission date and the receipt reference. Submission is refused after the closing date unless an extension is recorded first.
  - **The submitted version is locked.** If the institution asks for clarification or a revised price, record the clarification; a new version then needs approval and resubmission. The earlier submitted version stays on record.
- **Closing date extended:** record the new date and the addendum or notice reference. The original date stays in the history.

### Outcome
- **Won:**
  - Tick the awarded lines and adjust the awarded quantities, which cannot exceed the bid.
  - Enter the award letter reference, order date and delivery due date.
  - A **sales order** is created at the submitted bid prices, with no re-typing. Reserve, deliver and invoice it from Orders & quotations as usual.
  - Each awarded line needs a catalogue product, and the products' minimum prices apply.
- **Lost:** record the reason or feedback, the winning bidder and the winning amount, to learn from it for future bids.
- **Cancelled:** for example "decided not to bid", with a reason.

Every status change, version, approval and extension is kept in the tender's history, with who and when. The status bar shows Draft → Bid preparation → Approved → Submitted → Won.

### Documents and printing
- **Tender documents:** a panel on each tender holds the dossier, addenda, clarifications and the submission receipt; documents can also be linked to a tender from the Document library. Confidential documents, such as your priced bid, stay hidden from users without accounting or administration rights.
- **Price schedule:** a printable schedule for any version, with your company name, items, subtotal, tax, total, alternative offers and a signature line. Use **Print / save PDF**.

## Document library: previews and gallery
- **Cards:** each document shows a **picture of its first page**: PDFs, photos and TIFF scans. Word, Excel, PowerPoint and text files show a coloured file badge. Cards also show:
  - a coloured type badge, the title, reference, party and amount;
  - a deadline chip ("Due in 3 days", amber within a week, red when overdue);
  - the date and the number of files and links.

  **List** switches back to a table.
- **Opening a document shows it immediately**, beside its details, on page-style sheets:
  - **PDF:** up to 40 pages, drawn on this computer.
  - **TIFF scans:** multi-page, including LZW and deflate compression.
  - **Photos:** JPG, PNG, GIF, BMP and WebP.
  - **Word (.docx):** headings, bold, italic, underline, lists, tables and pictures.
  - **Excel (.xlsx):** each sheet as a table, up to 200 rows.
  - **PowerPoint (.pptx):** each slide's title and points.
  - **CSV and text.**
  - Older .doc and .xls files must be downloaded.
- **Other versions:** File versions → **Show** displays an earlier version.
- **Attached-documents panels** (transactions, cheques, customers, equipment, installations, tenders) now have **Show** to preview a file in place.
- **Offline and safe:**
  - Previews are produced on the computer. The bundled viewer libraries are pdf.js (Apache-2.0) and UTIF.js with pako (MIT), and nothing is sent to the internet.
  - Text from Word, Excel and CSV files is shown as text and never run as code.
  - The fingerprint check runs before every preview.
- **Picture cache:**
  - First-page pictures are made once and saved against the file's fingerprint, so the library opens quickly afterwards.
  - Pictures are derived data and can always be rebuilt, so they are not part of the sealed history.
  - Read-only users see pictures but do not save them.

## Database
Migration `TendersAndDocumentPreviews` adds `Tenders`, `TenderBids`, `TenderEvents` and `TenderAwards`, all append-only and protected by the database guards and integrity seals. It also adds `DocumentThumbnails`, a cache. Documents can now link to a tender.

## Verification
219 tests pass. New tests cover:
- **Tenders:**
  - costing arithmetic, including foreign cost, landed cost, margin, tax and excluded alternatives;
  - versions with reasons and stale saves refused;
  - approval rules, including separation of duties when another approver exists;
  - submission lock, clarification and resubmission;
  - award with quantity checks and conversion to a sales order;
  - loss with the winner's price, and cancellation;
  - closing-date extension;
  - cost visibility by permission;
  - tender document links with confidential documents hidden;
  - integrity verification.
- **Previews:**
  - Word, Excel, PowerPoint and CSV conversions, including script text in a document coming out as plain text;
  - unreadable files;
  - thumbnail validation, first-picture-wins, confidentiality, and read-only users not saving pictures.

Rendered-screen tests run a tender from registration to win and drive the card gallery and CSV preview. A browser run checked real files: a 3-page PDF tender, a Word proposal, an Excel price schedule, a cheque photo and a 2-page TIFF delivery note, each with thumbnails and previews.

## Limits
- Word previews show content and basic formatting, not exact page layout. Headers, footers, text boxes and tracked changes are omitted; the original download is unchanged.
- Older Office formats (.doc, .xls, .ppt) cannot be previewed.
- Tender lines without a catalogue product can be bid, but must be linked to a product before a win is converted.
- There are no automatic reminders before closing dates yet; the register and the library's deadline list show what is due.
