# View a company's financial statements

This guide shows you how to read a company's income statement, balance sheet, and cash-flow figures on its stock profile — the numbers Equibles extracts from the company's SEC filings (10-K annual and 10-Q quarterly reports) and standardises so you can compare periods.

These figures come from the structured XBRL data in SEC filings, so the tab fills in only for companies that file 10-K/10-Q reports. Funds and companies with no ingested filings yet show a "No Financial Data" message.

## Open the Financials tab

1. Search for a company by its ticker — for example, `AAPL` — and open its profile page.

2. Click the **Financials** tab (or go to `http://localhost:8080/stocks/{ticker}/financials`).

3. If structured financial facts have been ingested, you see the statement table with two selectors above it. If not, the tab shows a "No Financial Data" message.

## Choose a statement and period

Two dropdowns above the table control what you see:

- **Statement** — switch between **Income Statement**, **Balance Sheet**, and **Cash Flow**. Each shows a curated, ordered set of standard line items for that statement.
- **Period** — pick the fiscal period, such as a full year or a single quarter. The list shows the periods that have data, newest first.

Changing either dropdown reloads the tab with the new selection.

## Read the table

Each row is one line item from the selected statement. The columns are:

- **Line Item** — the standardised name of the figure (for example, *Revenue*, *Net Income*, *Total Assets*).
- **Value** — the reported amount. Money figures are shown as whole dollars (for example, `$1,234,567`); per-share figures such as earnings per share are shown to the cent (for example, `$1.23`). A dash (`—`) means the company did not report that line item for the selected period.
- **Unit** — the unit the figure is reported in, such as `USD` or `USD/shares`.
- **Period End** — the last day of the period the figure covers.
- **Form** — the SEC form the figure came from (for example, `10-K` or `10-Q`).
- **Filed** — when that filing was submitted to the SEC.

The **Unit**, **Period End**, **Form**, and **Filed** columns are hidden on narrow screens; widen the window to see them.

## Related

- To ask your AI assistant for the same figures, connect it and query the financial-statement tools — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- To walk through every tab on a stock profile, see [Explore a company's data on the web portal](tutorial-explore-stock.md).
