# Add a FRED API key (free economic-indicator data)

Get a free Federal Reserve Bank of St. Louis FRED API key and wire it into Equibles so the FRED scraper starts populating economic indicators.

1. Open [fred.stlouisfed.org/docs/api/api_key.html](https://fred.stlouisfed.org/docs/api/api_key.html) and click **Request API Key**. You'll need a free FRED account; the sign-up form asks for an email and a short description of what you'll use the key for ("self-hosted financial data dashboard" is fine).

2. Once approved (usually instant), copy the 32-character API key shown on the page. It looks like `abcdef1234567890abcdef1234567890`.

3. Open your `.env` file in the project root. Find this commented-out line:

   ```env
   # Fred__ApiKey=my-fred-api-key
   ```

   Uncomment it and replace `my-fred-api-key` with your real key:

   ```env
   Fred__ApiKey=abcdef1234567890abcdef1234567890
   ```

4. Restart the worker so it picks up the new variable:

   ```bash
   docker compose up -d --force-recreate worker
   ```

5. Within a few minutes, the FRED scraper starts. Visit `http://localhost:8080/economy` — you should begin to see categories (Interest Rates, Inflation, Employment, GDP, …) populating with series. The first full sync can take half an hour because there are several hundred series to pull.

6. To confirm the scraper is healthy, visit `http://localhost:8080/status` — the **FRED scraper** row should be `OK` and the **FRED indicators** count should be non-zero. If you see errors mentioning FRED, the most likely cause is a typo in the key; fix it and re-run step 4.

To remove the key (and stop the FRED scraper), comment out `Fred__ApiKey` and `docker compose up -d --force-recreate worker`. The existing data stays in the database.
