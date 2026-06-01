# Browse futures positioning (CFTC Commitments of Traders)

Use the Futures section of the web portal to view CFTC Commitments of Traders (COT) data — how commercial and non-commercial traders are positioned across futures markets.

## Open the Futures overview

1. Click **Futures** in the top navigation, or go to `http://localhost:8080/futures`.

2. Contracts are grouped into categories:

   | Category | Examples |
   |----------|----------|
   | **Agriculture** | Corn, wheat, soybeans, livestock |
   | **Energy** | Crude oil, natural gas, gasoline |
   | **Metals** | Gold, silver, copper |
   | **Equity Indices** | S&P 500, Nasdaq, Dow futures |
   | **Interest Rates** | Treasury notes and bonds, eurodollar |
   | **Currencies** | Euro, yen, pound, and other FX futures |
   | **Other** | Contracts that don't fit the categories above |

3. Each row shows the contract code, market name, the latest commercial net and non-commercial net positions, and the report date. Click any contract to open its detail page.

## View a contract's positioning

1. From the overview, click a contract row, or go directly to a URL like `http://localhost:8080/futures/<market-code>` (for example, the code shown next to the market name).

2. The detail page shows:

   - **Latest positioning** — open interest, commercial net, non-commercial net, and spreads from the most recent weekly report.
   - **Net positioning chart** — commercial net and non-commercial net positions plotted over the full report history.
   - **Reports table** — every weekly report (date, open interest, commercial long/short, non-commercial long/short, spreads, and the change in open interest) in reverse chronological order.

## What these numbers mean

- **Commercial** traders are hedgers — producers and users of the underlying commodity (for example, a farmer or an airline). Their net position often moves opposite to price.
- **Non-commercial** traders are large speculators (such as managed funds). A large net-long or net-short position signals where speculative money is leaning.
- **Open interest** is the total number of outstanding contracts; rising open interest means new money is entering the market.
- **Spreads** count non-commercial positions held simultaneously long and short across contract months.

## If the pages are empty

CFTC data is scraped automatically by the worker — no API key is required. After a fresh install, give the scrapers at least an hour to begin populating. Check ingestion progress on the [status page](how-to-view-status-and-errors.md).
