# Sierra Leone compliance and document templates — 0.20.0

This release implements specification **Skill 23** (Sierra Leone configuration and compliance). It also adds the production document templates from **Skill 24**.

Tax categories, rates, company identifiers, currency formats and document wording are now **settings with history**. Changing them never needs a new version of the program.

Open **Administration → Company & tax**. It has five tabs, described below.

## Company
- **Company details.** Legal and trading name, address, phone and email.
- **Identifiers.** Up to eight, such as **TIN**, **GST No.** and **Reg. No.**. Each can print on documents or be kept on file only. A name with no number is ignored.
- **Tax registration.** When the business is registered, sales and service invoices are titled with the *tax invoice title* (default "Tax invoice"). They show the tax name (default "GST") and at least one identifier.
- **Currency.** Code (SLE), symbol (NLe), decimal places, and whether the symbol goes before or after the amount. The currency and minor unit are given in words, with the option to print the **amount in words**, for example "One thousand two hundred Leones and fifty cents only".
- **Fiscal registration.** This can be switched on from a chosen date (see Fiscal below).
- **Who can change it.** Changing company details needs `users.manage`. Every version is kept with who saved it.

## Tax categories
- **Categories.** Every product is **standard-rated** unless set to **zero-rated** or **exempt**.
- **Scheduling a change.** A change applies from a date you choose (today or later) and needs a reason, such as the law or notice. A future change shows as scheduled.
- **Who can change it.** Needs `accounting.write`.
- **Rates.** Tax rates keep their start dates under Finance → Periods & tax, as before.
- **Sales.** Only standard-rated lines are taxed. The invoice marks zero-rated and exempt lines and shows the subtotal for each category. The rate and the category of each product are stored on the invoice, so **posted invoices never change** when a rate or category changes later.
- **Service invoices.** Labour is standard-rated. Parts follow their product's category.
- **Sales returns.** A return refunds only the tax that was actually charged on the returned lines. Returning exempt medicines refunds no tax. Sales from before 0.20.0 are treated as wholly standard-rated, exactly as they were posted.

## Tax return (needs `accounting.read`)
- **The summary.** For any period up to 13 months: standard-rated, zero-rated and exempt sales; tax on sales and service invoices; less sales returns; and the net for the period.
- **Document list.** Every document behind the figures is listed, with its fiscal status.
- **Ledger check.** The output tax is reconciled to the tax payable account (2100) on the same documents.
- **Before filing.** Check the figures against the current NRA return form.

## Documents
The letterhead and wording now appear on these printed documents:
- invoices and receipts;
- quotations, proforma invoices and orders;
- delivery notes, credit notes and payment receipts;
- service reports and invoices;
- installation certificates;
- tender price schedules.

What the template sets:
- **Letterhead.** Logo (PNG, JPEG or SVG under 200 KB), accent colour and tagline.
- **Wording.** Invoice terms, quotation terms, receipt note and delivery-note wording.
- **Payment details.** Bank, account name and number, printed on invoices and quotations.
- **Signatures and footer.** Signature lines, plus a footer line.
- **Receipt size.** **A4** or an **80 mm till roll**. With 80 mm, payment receipts print as a narrow till receipt.

The tab shows a live preview. Changing the template needs `users.manage`, and every version is kept.

## Fiscal registration
The program talks to fiscal devices or NRA services through a single connection point (`IFiscalIntegration`). No real device or service is included in this release.

When fiscal registration is switched on:
- **Sales never wait.** If no device or service is connected, sales and service invoices post normally and are kept as **pending**.
- **Sending pending invoices.** The Fiscal tab lists pending, registered and rejected invoices. **Send pending invoices now** (needs `sales.create` or `accounting.write`) sends the pending ones. Each invoice is registered once only, and every attempt is recorded.
- **Rejections.** An invoice that the device or service refuses is marked rejected, with its message.
- **On the invoice.** Printed invoices show the fiscal code, or "Fiscal registration pending".

**Connecting a real device or service.** It must be verified against the current NRA requirements. It is then added by registering an `IFiscalIntegration` for that device or service. No other code changes.

## Database
Migration `ComplianceAndDocumentTemplates` adds these tables:
- `CompanyProfiles`
- `DocumentTemplates`
- `TaxAssignments`
- `FiscalSubmissions`

All four are append-only and protected by the database guards and integrity seals.

## Verification
244 tests pass on Linux. The new tests cover:
- **Tax on sales.**
  - Only standard-rated lines are taxed.
  - A rate change and a category change take effect on their dates, and the earlier invoice is unchanged.
  - A scheduled change shows as upcoming.
  - Back-dated or duplicate changes, changes without a reason, and changes by a cashier are refused.
- **Returns and the tax return.**
  - Returns refund only the tax charged.
  - The tax return splits the categories and reconciles to the ledger.
  - The tax return is refused without `accounting.read`.
- **Company details and templates.**
  - Both are versioned and checked: stale saves, a registered business with no identifier printed, bad currency codes, duplicate identifiers, non-image logos, bad colours and unknown receipt sizes are all refused.
  - Anyone can read the letterhead to print, but only `users.manage` can change it.
- **Fiscal registration.**
  - Posting works with no device connected.
  - A pending invoice is sent once to a device, with the invoice lines, categories and seller TIN.
  - A rejection is recorded.
  - Invoices dated before the start date and reversed invoices are excluded.
  - The fiscal status shows in the tax return.
- **Amount in words.**
- **Screen test.**
  - The owner records the TIN and tax registration and makes medicines exempt.
  - The printed sale shows the letterhead, the "Tax invoice" title, the TIN, the exempt marking, the amount in words and "Fiscal registration pending".

## Limits
- **Fiscal connector.** No fiscal device or NRA online service is connected yet; invoices wait as pending until one is added.
- **Customer TIN.** The buyer's TIN is not yet captured on invoices.
- **Currency on screens.** The currency format applies to printed documents. Screens still show NLe with two decimals.
- **Unbilled service jobs.** The job sheet shows tax as an estimate at the current rate until the job is invoiced. The invoice itself applies the categories.
- **Numbering.** Document numbers keep their existing branch-device format. Configurable number sequences are not part of this release.
