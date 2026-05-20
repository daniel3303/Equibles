# Sync only a chosen list of tickers

Tell every Equibles scraper to limit itself to a specific set of stock tickers instead of pulling the whole universe. Useful when you only care about a handful of names and want a faster first sync, a smaller database, and less bandwidth.

1. Decide which tickers you want. Use the exchange ticker (e.g., `AAPL`, `MSFT`, `GOOGL`) — not the company name. Lowercase works; the scraper normalises case.

2. Open your `.env` file in the project root and add one entry per ticker, indexed from `0`:

   ```env
   Worker__TickersToSync__0=AAPL
   Worker__TickersToSync__1=MSFT
   Worker__TickersToSync__2=GOOGL
   Worker__TickersToSync__3=NVDA
   ```

   The double-underscore + numeric segment is how .NET configuration represents array entries. Indexes must be contiguous starting at `0`; a gap (e.g., 0, 1, 3) silently truncates the list at the gap.

3. Restart the worker:

   ```bash
   docker compose up -d --force-recreate worker
   ```

4. Open `http://localhost:8080/status` and watch the row counts. After the next scraper cycle (usually within a few minutes for short-window sources, longer for SEC), the per-domain counts stop growing once the requested tickers are covered. The **Stocks** page at `http://localhost:8080/stocks` shows only your chosen tickers.

5. To add or remove tickers later, edit `.env` and re-run step 3. Removing a ticker from the list doesn't delete data already in the database — the scrapers just stop fetching new rows for it. To wipe a stock's data entirely you'd need a manual SQL `DELETE` (out of scope for this guide).

To go back to syncing every ticker, delete (or comment out) all the `Worker__TickersToSync__*` lines and recreate the worker. The full-universe sync resumes on the next scraper cycle.

The filter applies to **all** scrapers — SEC filings, holdings, insider trades, prices, short data, congressional trades. CFTC, CBOE, and FRED data are not ticker-scoped, so they're unaffected.
