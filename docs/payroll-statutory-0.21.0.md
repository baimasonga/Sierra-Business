# Payroll with PAYE and NASSIT — 0.21.0

Payroll now works out PAYE and NASSIT and handles part months and one-off pay. It also pays salaries employee by employee and records the PAYE and NASSIT payments to the NRA and NASSIT. Open **People → Payroll**. It has four tabs.

## PAYE & NASSIT rules
- **Dated settings.** The rules have a start month. A later change applies from its own month, and payslips already posted keep the rules they were calculated with. A rule cannot start in a month that already has posted payroll.
- **Fill in current Sierra Leone rates.** This button enters:
  - **PAYE monthly bands:** the first SLE 600 at 0%, the next 600 at 15%, the next 600 at 20%, the next 600 at 25%, and anything above SLE 2,400 at 30%. These are the Finance Act 2021 bands, which the Finance Acts 2025 and 2026 left unchanged.
  - **Tax-free allowances:** up to SLE 500 a month.
  - **Non-residents:** a flat 25%.
  - **NASSIT:** 5% from the employee and 10% from the employer, on basic salary, stopping from the month after the employee turns 60.
- **Your responsibility.** Check the rates with the NRA and NASSIT before you rely on them. Every saved version records its source and who saved it.
- **Until rules are saved,** payroll behaves exactly as before, with no statutory deductions.

## How pay is worked out
1. **Basic salary and allowances** are paid for the calendar days employed in the month, less any unpaid days entered for the run (in half days).
2. **Gross pay** is basic salary plus allowances, overtime and bonus.
3. **NASSIT** is 5% of basic salary (or of gross pay, if the rules say so) from the employee, plus 10% from the employer. It is skipped for employees marked as not contributing, and after the age limit.
4. **Chargeable income** is gross pay, less tax-free allowances (up to the limit), less the employee's NASSIT.
5. **PAYE** is calculated band by band for residents, or at the flat non-resident rate.
6. **Net pay** is gross pay, less PAYE, the employee's NASSIT, the employee's regular other deductions and any one-off deduction. If deductions would come to more than the pay, posting is refused.

For example: basic salary 3,000 and allowances 800 give gross pay of 3,800. NASSIT is 150, and the employer's NASSIT is 300. Chargeable income is 3,800 − 500 − 150 = 3,150. PAYE is 90 + 120 + 150 + 225 = 585. With other deductions of 100, net pay is 2,965.

**Calculate** shows the whole run before anything is posted. Each payslip stores its full calculation.

## Accounting
**Posting a payroll** records:
- **Debits:** wages and salaries (5200, by department) and employer NASSIT contributions (5210, new).
- **Credits:** net pay payable (2200), **PAYE payable (2310, new)**, **NASSIT payable (2320, new**, employee and employer shares), and other deductions payable (2300).

**Paying salaries** (Payslips & payments):
- Choose the payslips to pay, by bank transfer (with a reference) or cash.
- The payment clears net pay payable against the bank or cash account.
- Each payslip shows when it was paid and by which document. It cannot be paid twice.

**Remitting PAYE and NASSIT** (PAYE & NASSIT):
- Each month's schedule lists every employee with their TIN, NASSIT number, gross pay, chargeable income, PAYE and both NASSIT shares.
- **Record a remittance** pays the outstanding amount for the month against the bank or cash account. The receipt or transfer reference is required.
- **Export to Excel** gives three sheets: the PAYE schedule, the NASSIT schedule and the remittances, with what is outstanding.

**Reversals:**
- A payroll with paid salaries cannot be reversed until the salary payment is reversed.
- A month whose PAYE or NASSIT has been remitted cannot have its payroll reversed until the remittance is reversed.

## Employees
These new fields appear on employee records. Existing employees count as residents and NASSIT contributors.
- TIN
- NASSIT number
- date of birth (optional; used only for the NASSIT age limit)
- resident for tax
- not a NASSIT contributor

## Payslips
Payslips print on the company letterhead. They show:
- earnings and deductions side by side;
- days paid;
- chargeable income;
- employer NASSIT;
- payment status;
- net pay in words.

## Database
Migration `StatutoryPayroll`:
- **New tables:** `PayrollRules`, `PayrollPaymentLines` and `StatutoryRemittances`. They are append-only and sealed.
- **New employee fields** (listed above).
- **New ledger accounts:** 2310 PAYE payable, 2320 NASSIT payable and 5210 Employer NASSIT contributions.

Payslips from before 0.21.0 still show as they were posted.

## Verification
250 tests pass on Linux. The tests cover:
- **PAYE bands.**
- **Posting.** The full calculation and its posting to each account; posted payslips stay unchanged after a rule change.
- **Rule checks.** Rules cannot start in a posted month, stale saves are refused, and bands, rates and the source are checked.
- **Pay adjustments.** Part-month joiners, unpaid days, overtime, bonus and one-off deductions; the flat non-resident rate; the NASSIT age limit.
- **Payments and remittances.** Salaries paid by bank or cash and never twice; PAYE and NASSIT remitted once, with nothing left outstanding afterwards; the three-sheet export; the order in which reversals must be made.
- **Screen test.** Fill in the rates, hire an employee, calculate and post the month, pay the salary and print the payslip.

## Limits
- **Payment files.** Payment files for banks, and NRA or NASSIT e-filing, are not produced. Use the Excel schedules.
- **Leave pay, end-of-service benefits and loans.** Leave pay caps and end-of-service benefits (now tax-exempt under the Finance Act 2026) are not calculated. Salary advances and loans are entered as regular other deductions or one-off deductions.
- **Payroll before 0.21.0.** Salaries for earlier payroll were paid through Accounting vouchers, so they are not linked to payslips.
