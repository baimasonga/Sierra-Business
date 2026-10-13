# Payroll, advanced accounting and foreign exchange continuation

Verified on 10 October 2026 against the current working tree.

## Implemented source workflows

- Workforce: attendance and overtime, leave entitlements and request decisions, configurable earnings and deduction formulas, and payroll integration.
- Advanced accounting: nested account groups, ledger mapping, cost-centre allocation, scenario projections kept separate from posted balances, and interest calculations over payment and reversal dates.
- Foreign exchange: foreign customer invoices, customer collections, supplier payments, currency-bank deposits/withdrawals/transfers, and closing valuation with an automatic next-day reversal. Cancellation reverses both valuation journals. Source currency units are retained while inventory and journals remain in local currency.

## Verification

The focused integration run passed 46 tests with zero failures and zero skips. It built the application, API, infrastructure, shared UI, desktop services and web host successfully. The selection covered `ForeignExchange`, `AdvancedAccounting`, `Workforce` and `FinanceTests`.

The FX tests cover invoice tax and local minimum-price checks, partial settlements, exchange gains, foreign-bank carrying amounts, mismatched currencies, stale previews, insufficient units, invoice returns, uninvoiced receipts, paired valuation cancellation, history protection, branch isolation, permission revocation and screen posting.

The full integration regression run passed 355 tests, failed zero and skipped six, out of 361 tests, in 15 minutes 9 seconds. The skipped tests are two live SQL Server checks and four live PostgreSQL checks; those providers were not configured for this run. The result file is `tests/SierraBusiness.IntegrationTests/TestResults/continuation-2026-10-10.trx`.

## Remaining scope

This verifies source workflows, not a newly packaged Windows release. Installed-app acceptance, physical devices, clean-machine upgrades, live database-provider checks and release signing remain separate work. Broader gaps such as company consolidation, alternative inventory valuation, consignment and statutory tax workflows remain open.
