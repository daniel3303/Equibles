# View market-wide insider trading activity

Use the insider trading dashboard to see the largest insider buys, sells, and overall transactions across all tracked stocks from the last 90 days.

## Open the dashboard

1. Go to `http://localhost:8080/insider-trading/dashboard` in your browser, or click **Insider Trading** in the top navigation.

2. You'll see three sections:

   - **Top Buys** — the largest insider purchases by dollar value.
   - **Top Sells** — the largest insider sales by dollar value.
   - **Biggest Transactions** — the largest transactions of any type, ranked by total value.

Each table shows the insider's name, the stock ticker, the transaction value, and the date. On wider screens you'll also see share counts and per-share prices.

## Read the tables

- **Green values** in the Top Buys card indicate purchases (money flowing in from an insider).
- **Red values** in the Top Sells card indicate sales.
- In the Biggest Transactions table, a green **Buy** or red **Sell** badge marks each row's direction.

All transactions come from SEC Form 4 filings — the mandatory disclosure that company insiders (officers, directors, and 10%+ shareholders) file within two business days of a trade.

## Drill into an insider or stock

- Click an **insider's name** to open their profile page, which lists all of their transactions across every company.
- Click a **ticker** to open that stock's profile, where you can see the per-stock insider trades tab alongside price history, holdings, filings, and more (see [Explore a company's data](tutorial-explore-stock.md)).

## If the dashboard is empty

The dashboard shows "No insider transactions yet" until the worker has ingested Form 4 filings. After a fresh install, give the scrapers at least an hour to begin populating insider data. You can check ingestion progress on the [status page](how-to-view-status-and-errors.md).
