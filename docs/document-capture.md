# Document capture — Windows preview 0.2.0

Use **Scan & capture** in the Windows sidebar. Import a PDF/image or use **Scan one page** with a Windows Image Acquisition (WIA) scanner. For a scanner feeder or multi-page job, save a PDF in the scanner's software and import it.

Text recognition runs locally using bundled Tesseract and English language data. PDFs are rendered locally with Windows PDF APIs; this release OCRs both scanned and digital PDFs. PNG, JPEG, BMP and multi-frame TIFF are supported. Limits: 20 MiB per original, 100 pages, 40 megapixels per input image and 300,000 extracted text characters. Exceeding a limit rejects the import rather than silently dropping pages. Cancellation takes effect between native OCR page operations.

The inbox retains the original file, SHA-256, source OCR, page count, reading score, uploader and creation date. Exact duplicate files open the existing capture. Local pattern matching suggests document type and clearly labelled reference, party, date, currency, total and deadline. It does not reliably interpret arbitrary line-item tables or tender clauses: these remain in the extracted text for manual review, with optional online interpretation. The displayed score is an OCR reading score, not field accuracy. Handwriting, low-resolution scans, rotated text and complex tables may require manual correction.

Open the original alongside the editable details. Save a draft or mark the capture reviewed. Every save creates an immutable revision with actor/time; previous values remain available. A reviewed capture is a source document, **not a posted transaction**. Automatic conversion into catalogue-matched trading documents and a complete tender management workflow are not in this release. Dates, SLL/SLE and amounts stay as printed; the system does not guess conversions or deadlines.

## Optional online assistance

Expand the online section, inspect/redact the extracted text, enter your own OpenAI API key/model, tick the request-specific consent checkbox and click **Send text for AI suggestions**. Only that text is sent to the fixed OpenAI Responses endpoint; no image, PDF, other business record or key is placed in the business database. Keys are held in screen memory for the request and cleared from the input. Nothing is sent automatically. Online assistance accepts at most 60,000 characters, and uses a 90-second timeout. It does not repair image quality because the image is not uploaded.

The request sets `store:false`, supplies no tools and uses a strict JSON schema. This setting does not promise zero provider retention; the provider's applicable API data policy still applies. Refused, incomplete, malformed and unsuccessful responses do not overwrite the capture. Before transmission, the local draft and request intent are saved in revision history. Successful suggestions become a new **unreviewed** revision. Review them against the original. User-supplied document text is treated as untrusted data, never as app instructions.

The default model field is `gpt-4.1-mini`; use a schema-capable model available to your API project. API usage is billed separately. No live paid request was made during development; request/response handling was exercised with fixtures.

## Storage and permissions

This small desktop preview deliberately selects bounded SQLite attachment storage behind `IDocumentStorage`: originals are in a separate capture table, limited to 20 MiB each, so original/metadata/initial revision commit atomically and the existing verified `.db` backup includes every original and review revision. This avoids backups that contain records but lose their scans. Database size grows with originals and revision history. Large attachment libraries need a filesystem/network/Blob implementation and coordinated backup before deployment at scale. Central attachment synchronization is not implemented.

Reading captures requires a current signed-in session and `business.read`; importing, reviewing and requesting AI additionally require `catalogue.write` in this preview. Authorization is refreshed for every service action, with branch-scoped reads. More granular document-specific permissions remain future work. Opening an original creates a Windows per-user cache copy for the external PDF/image viewer. The local database and cache have the same Windows-user protection as the existing desktop records; they are not separately encrypted.

## Verification

- 49 integration tests passed, including original retention/deduplication, immutable review history, stale revisions, expired/signed-out access, source-inclusive backup, limits, SLL preservation, explicit AI consent and invalid/refused/incomplete responses.
- Windows smoke runner processed a real synthetic two-page PDF and PNG with the shipped OCR engine, recognized the reference and total, and verified cancellation and the 100-page limit. Its sample reading score was 94%; this is not an accuracy benchmark for business documents.
- Native MAUI Release build passed with zero warnings/errors before packaging.
- WIA scanner hardware, a live paid AI request and a clean-machine installer/upgrade remain unverified. No customer documents or live database records were used for these tests.

Run `dotnet test tests/SierraBusiness.IntegrationTests -c Release` and `dotnet run --project tests/SierraBusiness.WindowsCaptureSmoke -c Release` on Windows. The packaging script runs both, verifies the English model hash and bundles the native OCR DLLs, language model and Microsoft Visual C++ runtime alongside .NET/MAUI/WebView2.

References: [Tesseract wrapper requirements](https://github.com/charlesw/tesseract), [Windows PDF rendering](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage.rendertostreamasync), [OpenAI structured outputs](https://developers.openai.com/api/docs/guides/structured-outputs), [Microsoft C++ deployment](https://learn.microsoft.com/en-us/cpp/windows/choosing-a-deployment-method).
