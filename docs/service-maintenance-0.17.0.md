# Service jobs and preventive maintenance — 0.17.0

Completes specification **Skill 20** (installation, warranty, service and maintenance) and Phase 6. Installations and customer warranties arrived in 0.14.0; this release adds the rest of after-sales.

**After-sales → Service jobs.**
- Anyone who can sign in can view jobs.
- Logging and working on jobs (assigning, diagnosis, labour, parts, completion) needs `sales.create` or `inventory.receive`.
- Closing and invoicing a job, and running maintenance plans, need `sales.create`.
- Free-of-charge (goodwill) service, and warranty or contract exceptions, need `pricing.manage`.
- Part costs are shown only to users with `sales.create`, `pricing.manage` or `accounting.read`.

## Service jobs
### Log the job
- **A unit you supplied:** pick it from the list and the customer is filled in. The screen shows straight away whether the unit is still under customer warranty.
- **Equipment bought elsewhere:** describe it and enter its serial number.
- Also record the site, contact, problem reported, priority (low, normal, high or urgent) and date reported. You can assign the engineer at the same time.

The status bar runs Logged → Assigned → In progress → Completed → Closed. Urgent and high-priority jobs are marked in the list. The list can be filtered by open, awaiting billing, closed or all jobs, and searched by customer, equipment, serial or job number.

### Do the work
- **Engineer and diagnosis:** assign or reassign the engineer and record the diagnosis.
- **Labour:** record hours, engineer, work done and an hourly rate.
- **Parts used:** issue parts from any warehouse.
  - **Stock moves once.** Each issue is an ordinary stock document that removes the part at its average cost into *Service jobs in progress* (account 1260). A retried issue is never counted twice.
  - Serial, batch and expiry rules apply as for sales, and expired stock cannot be issued.
  - Unused parts go back with **Return to stock**, at the cost they were issued at.
  - Closing a job never moves stock again. Part issues cannot be reversed from the documents list; return them from the job instead.
- **Completion:** record what was done, the date and the customer representative who accepted the work. Every item on the job's checklist must be ticked before the job can be completed.
- **Reopening:** if the customer is not satisfied before billing, reopen the job with a reason.

### Warranty claims and who pays
Who pays is decided explicitly, recorded with who decided it, and can be changed until the job is closed.

| Billing | Rule |
|---|---|
| **Chargeable** | Parts at their charge price, plus labour. |
| **Warranty claim** | The unit must have a valid customer warranty on the date reported, for the serial it was sold under. You choose whether the warranty covers parts, labour or both; anything not covered is charged. |
| **Maintenance contract** | For units with an active maintenance plan. Planned visits are set to this automatically. |
| **Goodwill** | No charge. Needs pricing authority and a reason. |

If the warranty or plan does not apply, a manager with pricing authority can still approve a **warranty or contract exception**, with a reason. The history shows it as an exception.

Supplier warranty claims are recorded separately: lodge a claim with the supplier and RMA reference, then record the outcome (accepted, repaired, replaced, credited or rejected). The job shows the supplier warranty end date.

### Closing and invoicing
- **Something is chargeable:** closing issues a **service invoice**.
  - The amount is charged parts plus charged labour, plus sales tax at the configured rate.
  - Payment can be cash, cheque (which registers the cheque), bank transfer, or on account.
  - On-account invoices follow the credit-sale rules: a registered customer, no credit hold, and within the credit limit. They appear in outstanding bills and are collected with receipts, cheques and credit notes exactly like credit sales.
- **Nothing is chargeable:** the cost of the parts used is written off to *Warranty and service costs* (5400). A job with no parts and no charge closes without any posting.
- **Reversing a service invoice:** this is done from the documents list, like any document. The job goes back to "completed, awaiting billing" so it can be billed correctly.
- **Service report:** a printable report and invoice shows the problem, diagnosis, work done, checklist, parts and labour, totals, billing basis, settlement, acceptance and signature lines.
- **Attachments:** signed reports, photos and other evidence can be attached to the job.

### Accounting
| Event | Debit | Credit |
|---|---|---|
| Part issued | Service jobs in progress (1260) | Inventory |
| Part returned | Inventory | Service jobs in progress |
| Chargeable close | Cash, bank, cheques in hand or receivables | Service income (4300), tax payable |
|  | Cost of sales (charged parts); warranty and service costs (covered parts) | Service jobs in progress |
| No-charge close | Warranty and service costs (5400) | Service jobs in progress |

## Preventive maintenance
- **Starting a plan:** choose a unit supplied to a customer, then set the site, the visit interval (1–60 months), the first visit, the engineer, notes and a visit checklist. Each unit can have one active plan.
- **Generating visits:** **Generate due visits** creates a service job for every visit due up to the date you choose (up to 90 days ahead). Each visit gets the plan's checklist and engineer and is billed to the maintenance contract.
  - Each visit is created once. Running it again never duplicates a visit.
  - Visits stop when the unit is returned or changes hands.
- **The plans list** shows the next visit (amber within 30 days, red when overdue), the number of visits, the last completed visit and status. **End** a plan with a reason.

## Equipment lookup
A serial unit's timeline now includes its service jobs: the reported problem, warranty claims accepted, completions with who accepted them, supplier claims and cancellations.

## Offline
Like the rest of the desktop app, service jobs work on the local database without a connection. Evidence attached on the desktop is kept with the job.

## Database
Migration `ServiceJobsAndMaintenance` adds:
- `ServiceJobs`, `ServiceJobEvents`, `MaintenancePlans` and `MaintenancePlanEnds`, all append-only and protected by the database guards and integrity seals;
- ledger accounts 1260 *Service jobs in progress*, 4300 *Service income* and 5400 *Warranty and service costs*.

Documents can now be linked to a service job.

## Verification
226 tests pass. New tests cover:
- **Warranty job:**
  - parts issued once even when the issue is retried;
  - insufficient stock refused;
  - part-issue reversal refused;
  - returns limited to what is outstanding, at cost;
  - completion requires the customer's acceptance;
  - closing requires a billing decision;
  - covered parts written off to 5400, with work in progress back to zero and stock unchanged on closing;
  - service history on the serial timeline;
  - integrity verification.
- **Chargeable on account:**
  - unregistered customers refused;
  - credit limit enforced;
  - invoice amounts and postings;
  - the outstanding bill with payment terms;
  - partial receipt allocation;
  - reversal blocked while a receipt is allocated.
- **Coverage rules:**
  - warranty, contract and goodwill refusals without authority;
  - exceptions require a reason;
  - partial warranty billing labour only;
  - a reversed invoice returns the job for billing, then it is re-closed as goodwill.
- **Preventive maintenance:**
  - plan validation;
  - due visits generated once across repeated runs;
  - contract billing and checklists;
  - next-due and last-visit tracking;
  - ended plans stop generating;
  - the 90-day limit.
- **Cancellation and claims:**
  - cancellation needs the parts back;
  - supplier claim and outcome;
  - read-only users cannot change jobs and do not see costs.

Screen tests drive a warranty repair from logging to closing and a maintenance plan with generated visits. A browser run checked:
- a warranty repair with a power board and fuses;
- a chargeable autoclave repair invoiced for cash;
- a three-monthly analyser maintenance plan.

## Limits
- Labour is billed at the rate entered on each entry; there are no standard labour rate tables yet.
- Engineer time is not costed. Wages stay in payroll, so labour shows as revenue only.
- Supplier warranty claims are records only. Credit received from the supplier is entered through supplier credit notes.
- Maintenance visits are generated when someone presses **Generate due visits**. There is no automatic background run or reminder yet.
- Maintenance contracts are not invoiced from the plan. Bill contract fees as normal sales.
