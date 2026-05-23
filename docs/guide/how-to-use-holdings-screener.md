# Use the 13F holdings screener to find institutional positions

The holdings screener lets you filter the full universe of U.S. stocks by institutional-ownership metrics — how many funds hold a stock, how much they own, and how those numbers changed quarter over quarter.

## Open the screener

1. Go to `http://localhost:8080` and click **Holdings** in the top navigation, then **Screener** (or go directly to `http://localhost:8080/holdings/screener`).

2. The screener needs at least two 13F report dates to work. If you see a warning that data isn't available yet, the worker is still backfilling 13F filings — check back in an hour or so.

## Set your filters

All filters are optional. Leave a field blank to skip that constraint. Filters are combined with AND logic — a stock must pass every filter you set.

| Filter | What it controls |
|--------|-----------------|
| **Report quarter** | The quarter to screen. Defaults to the most recent 13F filing date. |
| **Comparison quarter** | The quarter used for all "Δ" (change) columns. Defaults to the quarter before the report quarter. |
| **Filers (min / max)** | Number of institutions holding the stock in the report quarter. |
| **Δ Filers (min / max)** | Change in filer count between the comparison and report quarters. Positive = more institutions bought in. |
| **Total $ value (min / max)** | Aggregate dollar value of all institutional positions in the stock. |
| **Δ $ value (min / max)** | Change in total dollar value between the two quarters. |
| **% of float (min / max)** | Percentage of the stock's float held by institutions. |
| **New positions (min)** | Number of institutions that opened a brand-new position in the report quarter. |
| **Sold-out positions (min)** | Number of institutions that completely exited the stock in the report quarter. |
| **Industry** | Narrow results to one or more industries (multi-select). |

3. Fill in the filters you care about and click **Apply**. The URL updates with your query, so you can bookmark or share the filtered view.

## Read the results

The results table shows one row per stock that passes your filters. Columns mirror the filters: filer count, total value, percent of float, and their quarter-over-quarter changes.

- Click a column header to sort by that column.
- Click a stock ticker to jump to its full profile page, where you can see individual institutional holders, price history, SEC filings, and more.

## Export the results

Click **Download CSV** above the results table to save the filtered data as a CSV file. The export includes the same rows and columns currently shown, so set your filters before downloading.
