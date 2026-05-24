# Browse market indicators (VIX and put/call ratios)

Use the Market section of the web portal to view CBOE market indicators — the VIX volatility index and put/call ratios by category.

## Open the Market overview

1. Click **Market** in the top navigation, or go to `http://localhost:8080/market`.

2. You'll see two sections:

   - **VIX** — the latest VIX close, the previous close, and the 52-week high and low.
   - **Put/Call Ratios** — the latest ratio, call volume, and put volume for each category (equity, index, total, VIX, and ETP).

3. Click on a card to open its detail page.

## View VIX history

1. From the Market overview, click the VIX card, or go directly to `http://localhost:8080/market/vix`.

2. The detail page shows:

   - **Chart** — daily VIX closing prices over the full history (back to 1990), with 20-day and 50-day simple moving averages.
   - **Summary statistics** — mean, median, min, max, and standard deviation across all data.
   - **Latest vs. previous** — the most recent close alongside the prior day's close.
   - **Data table** — every daily observation (date, open, high, low, close) in reverse chronological order.

## View a put/call ratio

1. From the Market overview, click any put/call ratio card, or go directly to a URL like `http://localhost:8080/market/putcallratio/Equity`.

2. The available ratio types are:

   | Type | What it measures |
   |------|-----------------|
   | **Equity** | Options on individual stocks |
   | **Index** | Options on market indexes (e.g., SPX) |
   | **Total** | All CBOE options combined |
   | **Vix** | Options on the VIX itself |
   | **Etp** | Options on exchange-traded products (ETFs/ETNs) |

3. Each detail page shows a chart of the ratio over time, summary statistics (mean, median, min, max, standard deviation), the latest vs. previous ratio, and a full data table with call volume, put volume, total volume, and the put/call ratio per day.

## What these numbers mean

- **VIX** measures the market's expectation of 30-day volatility, derived from S&P 500 option prices. Higher values indicate more expected volatility (often called the "fear gauge").
- **Put/call ratio** is the volume of put options divided by the volume of call options. A ratio above 1.0 means more puts than calls were traded (often interpreted as bearish sentiment); below 1.0 means more calls (bullish sentiment).

## If the pages are empty

CBOE data is scraped automatically by the worker — no API key is required. After a fresh install, give the scrapers at least an hour to begin populating. Check ingestion progress on the [status page](how-to-view-status-and-errors.md).
