# Equipment lifecycle, installation and warranty — 0.14.0

This is the first of three Phase 6 releases. It implements specification **Skill 17** (medical and IT equipment serial lifecycle) and the installation and warranty parts of **Skill 20**. Service jobs and preventive maintenance follow in 0.15.0, and tenders in 0.16.0.

The new **After-sales** area has three pages. Anyone who can sign in can look up equipment. Scheduling and completing installations, and starting warranties, need `sales.create`. Equipment setup needs `catalogue.write`. No new permissions were added, so existing users keep their access.

## Equipment setup
**Warranty and installation terms**, set for each product:
- **Customer warranty:** 0 to 120 months; 0 means none.
- **When it starts:** on the sale or delivery date, or when installation is completed. A warranty that starts at installation needs "installation required".
- **Supplier warranty to us:** in months, counted from the date the unit was received.
- **Coverage notes.**
- **Installation and commissioning required.**
- **Commissioning checklist:** up to 30 checks.

Each save adds a version, and a save based on an outdated version is refused. Existing warranties and scheduled installations keep the terms they started with.

**Compatible items:** link equipment to its **accessories**, **consumables** or **spare parts**, for example a printer to its toner. These are real product links, not free text. A removed link stays in the audit history together with the reason.

## Equipment lookup
Search by serial number, SKU or product name. Opening a unit shows:
- **Where it is now:** in stock (and which warehouse), in service, with a customer, installed, returned to the supplier or removed by a stock count.
- **Who has it:** the customer, the sale date and the sale document.
- **Where it came from:** the supplier and receipt date. The cost appears only for users with `accounting.read`.
- **Supplier warranty end date.**
- **Warranty coverage today:** covered until a date with the days left, expired (repairs are chargeable), or none recorded.
- **Unit specifications:** for example processor, RAM, X-ray tube or firmware, plus the accessories supplied and notes. These are versioned and kept for audit.
- **Complete history:**
  - receipt, sale or delivery, returns, transfers, stock counts, cost adjustments and reversals;
  - installation scheduled, completed or cancelled;
  - warranty start.
- **Compatible items** with the quantity in stock. Looking up a consumable shows which equipment uses it.

## Installations
1. **Schedule:** choose a sale or delivery note. Only its serial units still with that customer are offered. Enter the site, planned date, engineer and notes, then print the **job sheet** for the engineer.
2. **Complete:**
   - Tick every commissioning check; the app refuses to complete while any check is outstanding and lists them.
   - Enter the completion date, who accepted the work for the customer (required), and who was trained.
   - Print the **installation and commissioning certificate** for the customer to sign.
3. **Cancel** a scheduled installation with a reason; its units can then be scheduled again.

A unit can be on only one live installation for a sale. A sale with a *scheduled* installation cannot be reversed until that installation is cancelled.

## Warranties
- **Terms that start at sale:** the warranty starts automatically when a sale or delivery note with the serial is posted. An invoice raised later for that delivery does not start a second warranty.
- **Terms that start at installation:** the warranty starts automatically when the installation is completed.
- **Sales made before terms were set:** use **Start customer warranty** on the unit.
- **Void:** a warranty becomes void when its sale is reversed or the unit comes back, for example through a sales return. Reselling the unit starts a new warranty for the new customer; the old one stays as void history.
- Warranty records are append-only, and each one references the exact sold serial.

## Fix included
Printed invoices, delivery notes and orders now show the real company name and branch. Before this, some of them showed the literal words "CompanyName" and "Actor.Branch".

## Database
Migration `EquipmentLifecycle` adds the following tables, all protected by the database guards and the integrity seal chain:
- `EquipmentTerms`;
- `ProductLinks` and `ProductLinkRemovals`;
- `UnitProfiles`;
- `Installations`, `InstallationUnits`, `InstallationCompletions` and `InstallationCancellations`;
- `Warranties`.

## Verification
203 tests pass. New tests cover:
- serial received → sold → installed with checklist enforcement → warranty starting at installation, with the supplier warranty date and the full timeline;
- return then resale: the first warranty is void and the second is active; reversing the resale voids it too;
- starting a warranty for a sale made before terms existed;
- reversal blocked by a scheduled installation, and allowed after cancellation;
- compatibility in both directions, including duplicate and self-link refusal and removal;
- unit specifications with version conflicts;
- search;
- terms versioning and validation;
- permissions, including cost hidden without `accounting.read`.

Integrity verification passes after each scenario. Rendered-screen tests schedule and complete an installation and print the certificate, and look up a unit and save its specifications. The browser host was checked visually at desktop and phone widths.

## Limits (next releases)
- Service and repair jobs, parts used, warranty claims and preventive maintenance arrive in **0.15.0**.
- Evidence photos and documents are not attached to installations yet; use Documents → Scan & capture for now.
- Field screens need the desktop app; separate offline field-device syncing belongs to Phase 5.
