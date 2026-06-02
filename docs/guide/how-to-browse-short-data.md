# Browse market-wide short data (most shorted and largest short volume)

This guide shows you how to use the two market-wide short-data pages: **Most Shorted**, which ranks stocks by FINRA bi-monthly short interest, and **Largest Short Volume**, which ranks stocks by FINRA consolidated daily short volume.

Both pages come from FINRA data, so they need a FINRA API key to fill up. If you have not set one yet, follow [Add a FINRA API key](how-to-set-up-finra-api-key.md) first — without it the scraper skips and these pages stay empty. (Fails-to-deliver data comes from the SEC and works without FINRA, but it is shown per-stock, not on these two pages.)

## Open the pages

1. Go to `http://localhost:8080` and click **More** in the top navigation.

2. Pick one of:
   - **Largest Short Volume** — or go directly to `http://localhost:8080/short-volume`.
   - **Most Shorted** — or go directly to `http://localhost:8080/most-shorted`.

3. If you see a "No daily short volume has been ingested yet" or "No short interest data" message, the worker has not finished its first FINRA cycle yet (or the FINRA key is missing). Check back after the initial sync.

## Largest Short Volume

This page ranks stocks by their reported short volume for a single trading day.

1. Use the **Trading day** dropdown to pick which day to view. It defaults to the latest available day; older days stay available in the list.

2. Use the **Sort** dropdown to reorder the table. Stocks with zero total volume are always excluded, since they have no meaningful short percentage.

The table shows 50 stocks per page:

| Column | What it shows |
|--------|---------------|
| **Ticker** | The stock's ticker symbol. |
| **Name** | The company name. |
| **Short Volume** | Shares sold short during the day. |
| **Short Exempt** | Short-exempt shares (a subset of short volume exempt from the uptick rule). |
| **Total Volume** | All shares traded during the day. |
| **Short %** | Short Volume as a percentage of Total Volume. |

## Most Shorted

This page ranks stocks by FINRA short interest — the total open short position reported twice a month on a settlement date.

1. Use the **Settlement date** dropdown to pick which bi-monthly report to view. It defaults to the latest available settlement date.

2. Use the **Sort** dropdown to reorder the table.

The table shows 50 stocks per page:

| Column | What it shows |
|--------|---------------|
| **Ticker** | The stock's ticker symbol. |
| **Name** | The company name. |
| **Current Short Position** | Total shares held short as of the settlement date. |
| **Change vs Prev** | The change in short position since the previous settlement date. |
| **Days to Cover** | Current Short Position divided by average daily volume — how many days of normal trading it would take to buy back every shorted share. Blank when average volume is unavailable. |
| **Avg Daily Volume** | Average daily trading volume used to compute Days to Cover. |

## Short volume vs short interest

The two pages measure different things, so use them together:

- **Short volume** (Largest Short Volume) is a daily flow — how much shorting happened on one day. It updates every trading day.
- **Short interest** (Most Shorted) is a standing balance — how many shares are currently held short. FINRA publishes it twice a month, so it lags but shows accumulated bearish positioning.

For the short history of a single company, open that stock's profile and use its **Short Volume** tab — see [Explore a company's data on the web portal](tutorial-explore-stock.md).
