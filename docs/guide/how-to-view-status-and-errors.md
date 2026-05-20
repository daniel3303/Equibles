# Check worker health and recent errors

Use the built-in Status page to confirm scrapers are running, see how much data is in your database, and review any errors that have happened recently. This is the first place to look when something seems off.

1. Open `http://localhost:8080/status` in your browser. (If you've enabled login auth, sign in first — see [Enable login authentication on the web portal](how-to-enable-authentication.md).)

2. The top section shows the current Equibles version and tells you whether a newer release is available on GitHub.

3. The **Data counts** section lists one row per domain: stocks, daily prices, SEC documents, institutional holdings, insider transactions, congressional trades, FRED observations, FINRA short data, CFTC positioning, CBOE indicators, and any others a future release adds. Each row shows the current total. After a fresh install these climb steadily; after a few weeks they should change daily as new data arrives.

4. The **Errors** section lists the most recent errors any scraper or MCP tool has reported. Each error includes:

   - **Time** — when the error was first seen.
   - **Source** — which scraper or MCP tool reported it (`HoldingsScraper`, `YahooPriceScraper`, `MCP:GetTopHolders`, etc.).
   - **Message** — the exception or failure summary.
   - **Stack** — the full stack trace, collapsed by default; click to expand.

   An empty errors list is the goal. Transient errors (one-off network blips, single failed SEC requests) often clear on the next cycle and don't need action.

5. If you see the same error repeating across cycles, that's worth investigating. The most common patterns and their fixes:

   - **`SEC EDGAR returned 403 Forbidden`** — your `SEC_CONTACT_EMAIL` is missing or malformed. Fix it in `.env` and run `docker compose up -d --force-recreate web worker`.
   - **`FRED API returned 400 Bad Request`** or **`401 Unauthorized`** — your `Fred__ApiKey` is incorrect or empty. See [Add a FRED API key](how-to-set-up-fred-api-key.md).
   - **`FINRA OAuth failed`** — your `Finra__ClientId` / `Finra__ClientSecret` pair is wrong or has been rotated. See [Add a FINRA API key](how-to-set-up-finra-api-key.md).
   - **`CREATE EXTENSION "pg_search" does not exist`** at startup — you're running against a vanilla Postgres image instead of `paradedb/paradedb`. Use the official `docker-compose.yml`.

6. To clear the error list once you've fixed the underlying issue, errors disappear automatically as soon as the next successful scraper cycle completes — there's no manual "acknowledge" button.

If the Status page itself doesn't load, the `web` container is down. Run `docker compose ps` to check, and `docker compose logs web` to see why.
