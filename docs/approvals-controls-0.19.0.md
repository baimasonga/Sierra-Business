# Approvals and business controls — 0.19.0

This release implements specification **Skill 22**: audit trail, fraud controls and approval workflows. High-risk postings are now held for a second person's decision instead of going through silently. Everything is recorded and can be reviewed and exported.

This installer also carries **0.18.0** (dashboard and drill-down reports). Its own build was interrupted, so both releases are delivered together.

## Business controls
Set the controls under **Administration → Controls & exceptions**. Changing them needs `users.manage`.

| Control | A posting is held when… | Approved by someone with |
|---|---|---|
| Discount limit | a sale line is more than *N* % below the product's list price | `pricing.manage` |
| Below cost | a sale line is priced below its stock cost | `pricing.manage` |
| Back-dating | a sale, receipt or voucher is dated more than *N* days ago | `accounting.write` |
| Credit limit override | a credit sale takes the customer over their limit | `accounting.write` |
| Reversals | the reversed document is worth at least NLe *X* (0 = every reversal, or void) | `accounting.write` |
| Stock adjustments | a direct stock count changes stock value by NLe *X* or more | `accounting.write` |

How the settings work:
- **Credit limit override:** without it, a credit sale over the limit is refused outright, as before.
- **Recommended settings:** a 10 % discount limit, below-cost approval, 3 days for back-dating, every reversal, adjustments of NLe 500 and over, and the credit override.
- **Off by default:** controls stay off until you save them, so existing sites behave exactly as before.
- **Versioned:** every change is kept with who made it.

## How an approval works
1. **The posting is held.** A cashier posts a sale at 25 % off. It is **not posted**, and the screen explains why: "Approval needed: WID-1 at NLe 150.00 is 25 % below its list price NLe 200.00 (limit 10 %). Request APR-… has been sent". Trying again does not create a second request.
2. **An approver decides.** Under **Administration → Approvals → To decide**, the approver sees the request and its reasons, then approves it or rejects it. A rejection needs a reason.
3. **The requester posts it.** This can be done with **Post now**, or by posting the same entry again from its own screen. Only the identical posting goes through, and only once. If the details changed, or something new trips a control, approval is needed again.

Rules around approvals:
- **No self-approval:** you cannot approve your own request while another active user in the branch can approve it.
- **Working alone:** if nobody else can approve, self-approval is allowed and marked **self-approved**. The policy can switch this off.
- **Withdrawing:** requests can be withdrawn by the requester while they are waiting.
- **Notes:** the requester or an approver can add notes.
- **Dashboard:** approvers see "approvals waiting for you" there.

## Exceptions for review
**Controls & exceptions** (needs `audit.read`) lists for any period:
- reversals, with their reasons;
- back-dated postings;
- sales more than the limit below list price;
- below-cost sales;
- stock count adjustments;
- work outside the working hours set in the controls;
- every approval decision, with self-approvals marked.

Each row shows the user, the document (select it to open the document) and the approval reference used. A per-user table counts each kind and marks "⚠ Review" for:
- three or more reversals or back-dated postings;
- five or more after-hours actions;
- three or more self-approvals.

These are transparent business controls for a manager to look at, not findings against anyone.

**Export to Excel** (needs `audit.read`) saves a workbook with four sheets: Exceptions, By user, Approvals and Audit trail. The audit trail lists every recorded action with user, time and document.

## Integrity
- **Append-only records:** control settings, requests, decisions, uses and notes are append-only. They are protected by the database guards and integrity seals, like posted transactions.
- **No silent removal:** no screen can delete an approval or its evidence.
- **Unchanged screens:** the existing posting screens are unchanged. A held posting shows the approval message instead of being saved.

## Database
Migration `ApprovalsAndControls` adds `ControlPolicies`, `ApprovalRequests`, `ApprovalDecisions`, `ApprovalUses` and `ApprovalNotes`.

## Verification
All 238 tests pass on Linux. The Windows installer build ran 237 and passed all of them. It skips one dashboard screen test (`DashboardDrillsToDocumentsAndRefreshesWhenSomeonePosts`) because the bUnit renderer stalls under the Windows test host; that will be fixed in the next release.

New tests cover:
- **Discount approvals:**
  - a discount within the limit posts;
  - a larger one is held, with nothing posted and no duplicate request on retry;
  - the cashier cannot approve it;
  - a rejection needs a reason;
  - only the requester posts it;
  - changed details are not covered;
  - the identical sale posts once, even when retried.
- **Self-approval:** allowed and recorded when alone, refused when another approver exists, and refused when the policy forbids it.
- **Other sale controls:** below-cost, back-dated and credit-limit approvals, with the limit staying absolute when the override is off.
- **Vouchers:** back-dated vouchers are held.
- **Reversals and stock adjustments:** held above the threshold, posted after approval, and both shown in the exception report and per-user counts.
- **Permissions:** the exception report and export are refused without `audit.read`; the export has four sheets; policy changes need `users.manage` and are validated and version-checked.
- **Integrity:** verification after approvals.

A screen test switches on the recommended controls, then a manager approves a held sale on the Approvals screen. The cashier posts it with **Post now**, and the discount appears under Exceptions.

## Limits
- **Sales returns and customer refunds** are not yet held by a control. They appear in the audit trail.
- **Prices:** the discount check compares against each product's current list price. Tender-awarded orders priced below list will also ask for approval.
- **Working hours:** these are compared in GMT, which is Sierra Leone time.
- **Notifications:** there are no email or SMS alerts for waiting approvals. Approvers see them on the dashboard and the Approvals screen.
