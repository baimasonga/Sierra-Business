# Financial ratios — source increment after 0.30.0

Financial statements now includes a Financial ratios tab and Excel export. Six measures use branch-filtered posted journals: gross margin, net profit margin, return on average assets, asset turnover, liabilities to assets and liabilities to equity. Every row shows its formula, numerator, denominator and units. The report rechecks business/accounting read permissions and the configured branch.

Period measures use the selected dates and are not annualised. Average assets uses opening and closing balances, not daily averages. Net margin uses all income; gross margin and turnover use sales and service income. Equity includes accumulated profit, independently of year-end closure. Returns and reversal journals participate through their signed entries. Non-positive denominators return N/A; a loss with a positive denominator remains negative.

Current/quick ratios, stock turnover, comparative columns and custom ratio definitions remain outside this increment. Current/non-current account classification is required before liquidity measures can be reliable. No schema migration or posting change is introduced.

The verified 0.30.0 installer predates this source increment and does not contain the ratios tab. Native release packaging and device acceptance for this increment remain pending.

Validation: four targeted tests passed with no failures or skips (artifacts/verification/financial-ratios/financial-ratios-final.trx). Coverage includes known posted financial results and opening assets, empty periods, invalid dates, negative/zero denominator behavior, accounting permissions, branch rejection, workbook generation, and rendered tab/export invocation. The test build compiled shared UI, web and both provider projects. This is not a new full-suite or native-device certification. Git whitespace checking passed with the repository's existing CRLF line endings allowed.
