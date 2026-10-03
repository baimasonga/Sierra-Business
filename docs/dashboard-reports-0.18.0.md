# Dashboard and drill-down reports — 0.18.0

Implements specification **Skill 21** (reporting, dashboards, drill-down and live updates) and opens Phase 7. It adds two screens under **Overview**:
- **Dashboard**
- **Reports**

Every figure is calculated when you open it:
- money figures from the accounting journal;
- stock figures from stock movements.

Nothing is stored separately, so the reports cannot drift from the books. **Dashboards are not a source of truth**: the ledger and stock movements are.

## Period
Each screen has **From** and **To / as at** dates, and presets for:
- today;
- this month;
- the last 30 days;
- this year.

Balances (cash, receivables, payables and stock) are as at the **To** date.

## Dashboard
- **Net sales:** sales and service income, excluding tax and net of returns and reversals, with the number of invoices.
- **Gross profit and margin:** net sales less cost of sales.
- **Cash and bank:** cash, bank accounts and cheques not yet cleared.
- **Receivables and payables:** each with the amount overdue.
- **Stock value:** with the number of items at or below their reorder level.
- **Net sales chart:** daily bars for periods up to about two months, monthly bars for longer periods. Hover a bar for its figure and gross profit; select it to see its transactions.
- **Top customers and top products:** select one to see its documents or stock movements.
- **Operations:**
  - open service jobs and jobs awaiting billing;
  - open tenders, with those closing within seven days;
  - warranties ending within 30 days;
  - items to reorder.

  Each opens its own screen.
- **Checks:** stock value from movements against the Inventory ledger, and total debits against total credits. A difference is shown in red with the amount.
- **Branch comparison:** appears when more than one branch's records are in the database.

## Live updates
- **Automatic refresh:** the dashboard and reports refresh by themselves within about a second when anyone posts, on the desktop or in the web portal. In the web portal this travels over the portal's live (SignalR) connection.
- **Fallback:** figures are also re-read every two minutes, and **Refresh** is always available. A dropped connection therefore never leaves figures frozen, and the portal reconnects automatically.
- **Status:** a "● Live · updated" stamp shows when the figures were last read.

## Drill-down
Every figure leads to what makes it up. A path line above each report (for example *Dashboard › Sales by customer › Kissy Clinic*) lets you step back.

| From | Goes to |
|---|---|
| Net sales | Sales by day |
| A day | That day's documents |
| A month | Sales by day in that month |
| Gross profit | Sales by customer |
| A customer | Their documents |
| Cash and bank | The cash ledger |
| Receivables or payables | Ageing |
| A party in ageing | Their outstanding bills |
| Stock value or a product | That product's stock movements |
| Any document line | The document itself (Documents screen), with print, attachments and reversal |

## Reports
- **Sales analysis:** by day, month, customer or product.
  - **Day, month and customer:** read straight from the Sales and Service income ledgers. Users with `accounting.read` also see cost, gross profit and margin.
  - **Product:** read from the lines of unreversed sales invoices. Cost is the stock that left on each invoice (or its delivery note). A row "returns, reversals, service and other ledger adjustments" brings the total back to the ledger.
  - **Checks:** each grouping shows a reconciliation line. The product view checks the invoice lines against the Sales ledger postings on the same invoices.
- **Day book and ledgers:** every document in the period, newest first, with filters for type, customer/supplier and number.
  - **Paging:** 50 documents per page, fetched page by page from the database.
  - **Ledger view:** with `accounting.read`, choose a ledger account to see its opening balance, debits, credits and closing balance. Each document then shows its debit or credit on that account.
- **Stock valuation:** quantity, weighted average cost and value per product as at the date, with warehouse quantities. The total is checked against the Inventory ledger.
- **Stock movements:** for one product, the opening balance, receipts, issues and closing quantity and value. Each movement shows its document, warehouse, serial or batch and running balance. Shown 50 per page.
- **Receivables and payables ageing** (needs `accounting.read`):
  - outstanding bills by customer or supplier, in the columns not yet due, 1–30, 31–60, 61–90 and over 90 days past due;
  - expand a party to see its bills;
  - the total is checked against Trade receivables (1100) or Trade payables (2000). Any difference is explained as unallocated payments, credits or opening balances not matched to bills.

## Permissions
- **Dashboard, sales analysis, day book, stock valuation and stock movements:** anyone who can sign in.
- **Needs `accounting.read`:**
  - money positions (cash, bank, receivables, payables);
  - gross profit;
  - cost columns;
  - ledger views;
  - ageing;
  - the reconciliation checks;
  - branch comparison.
- No new permissions are needed.

## Other changes
- The Overview area now shows its screen tabs: Overview, Dashboard and Reports.

## Database
No schema changes.

## Verification
232 tests pass. New tests cover:
- **Ledger reconciliation:** dashboard net sales, cost, gross profit and margin, cash, receivables, payables and their overdue amounts, all against the ledger, including a reversed sale netting to nothing.
- **Stock:** stock value equals the Inventory ledger.
- **Trend:** daily and monthly trends.
- **Ranking and branches:** top customers and products, and the branch figures.
- **Restricted users:** no money figures or checks, and ageing and ledgers refused.
- **Sales analysis:** by customer, day and product, with all reconciliation checks passing.
- **Drill to documents and ledgers:** from a customer to the invoice, the receivables ledger with opening, debits, credits and closing, and the cash ledger showing a reversed document.
- **Stock reports:** stock valuation at two dates; stock movements with opening, in, out, closing and running balance.
- **Ageing:** buckets for receivables and payables, both reconciling to their ledgers.
- **Paging:** 28 documents in pages of 10 with no overlap, and a type filter.
- **Change notices:** raised on every posting.

Screen tests:
- drill from the dashboard to sales by day, to a customer, to the document, and back;
- the dashboard updating live after another posting;
- a read-only user's stock valuation drilling to movements, with no money figures.

A browser run with two months of trading checked the dashboard, ageing with expanded bills, sales by customer and product, stock movements and the cash ledger.

## Limits
- **Branches:** comparison covers the branches whose records are in this database. Multi-site consolidation arrives with the SQL Server and Azure work, which is on hold.
- **Product analysis:** shows invoice lines before returns. Returns appear in the adjustments row, not against each product.
- **Exports:** to keep a report on paper, use the browser or app print. Excel exports of reports are not yet included.
