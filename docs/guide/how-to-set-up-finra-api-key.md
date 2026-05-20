# Add a FINRA API key (free short-volume data)

Get free FINRA API credentials and wire them into Equibles so the FINRA short-volume and short-interest scrapers start populating data. The fails-to-deliver feed is SEC-sourced and already works without FINRA — this page is only for the FINRA side.

1. Open [gateway.finra.org/app/api-console](https://gateway.finra.org/app/api-console) and sign in. A Google account works; you don't need a FINRA-specific login.

2. From the top-right menu, open **API Credentials** and click **Create a new API Key**. Pick any name — something like `equibles` is fine.

3. Copy the **Client ID** and **Client Secret** shown on the confirmation screen. Save them somewhere safe; the Client Secret won't be shown again.

4. Open your `.env` file in the project root. Find these two commented-out lines:

   ```env
   # Finra__ClientId=my-finra-client-id
   # Finra__ClientSecret=my-finra-client-secret
   ```

   Uncomment both and paste in your real credentials:

   ```env
   Finra__ClientId=abcdef-12345-67890
   Finra__ClientSecret=your-long-secret-here
   ```

5. Restart the worker:

   ```bash
   docker compose up -d --force-recreate worker
   ```

6. Within a few minutes, the FINRA scraper will run its first cycle. Open `http://localhost:8080/status` — the **FINRA scraper** row should show `OK`, and the short-data counts should start climbing. To check at the stock level, open a stock's detail page (e.g., `http://localhost:8080/stocks/aapl`) and look at the **Short Volume** and **Short Interest** tabs.

The older `developer.finra.org` "Teams & Apps" flow has been retired. If a search result sends you there, use the API Console link in step 1 instead.

To remove the credentials, comment out both lines in `.env` and `docker compose up -d --force-recreate worker`. Existing short data stays in the database.
