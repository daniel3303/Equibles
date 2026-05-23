# View quarterly holdings activity across the market

This guide shows you how to use the 13F Quarterly Activity page to see which stocks institutions are buying and selling the most, and which stocks gained or lost institutional holders entirely.

## Open the activity page

1. Go to `http://localhost:8080` and click **Holdings** in the top navigation, then **Activity** (or go directly to `http://localhost:8080/holdings/activity`).

2. The page needs at least two quarters of 13F data to compare. If you see a "No 13F data yet" message, the worker is still backfilling — check back after the initial sync completes.

## Choose a report date

Use the **Report Date** dropdown at the top to pick which quarter to view. The page compares the selected quarter against the previous quarter and ranks stocks by the size of the change.

## Read the four boards

The page shows four ranked boards, each highlighting a different dimension of institutional activity:

| Board | What it shows |
|-------|--------------|
| **Top Buys** | Stocks where institutions increased their positions the most by dollar value. These are the largest net purchases across all 13F filers. |
| **Top Sells** | Stocks where institutions reduced their positions the most by dollar value. These are the largest net sales. |
| **New Positions** | Stocks that gained the most brand-new institutional holders — institutions that had zero shares in the prior quarter and now hold a position. |
| **Sold-Out Positions** | Stocks that lost the most institutional holders entirely — institutions that held shares in the prior quarter and now report zero. |

Each board lists stocks ranked by the relevant metric, with the ticker, company name, and the magnitude of the change.

## Export the data

Click **Download CSV** at the top of the page to export all four boards for the selected quarter as a single CSV file.
