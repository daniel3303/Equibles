# Browse economic indicators

Use the Economy section of the web portal to explore FRED economic indicators — interest rates, inflation, employment, GDP, and more — grouped by category with charts and summary statistics.

## Prerequisites

You need a [FRED API key](how-to-set-up-fred-api-key.md) configured. Without it, the FRED scraper is skipped and the Economy pages will be empty.

## Open the Economy index

1. Click **Economy** in the top navigation, or go to `http://localhost:8080/EconomicData`.

2. You'll see indicators organized by category (e.g., Interest Rates, Inflation, Employment, GDP). Each row shows:

   - The indicator's FRED series ID and title.
   - Its units and update frequency (daily, monthly, quarterly, etc.).
   - The latest observed value and date.

3. Click any indicator to open its detail page.

## Read an indicator's detail page

The detail page shows everything Equibles has collected for one FRED series:

1. **Chart** — a time-series chart of all observations, with optional 20-period and 50-period simple moving averages (SMA-20, SMA-50).

2. **Summary statistics** — mean, median, min, max, and standard deviation across the full history.

3. **Latest vs. previous** — the most recent observation alongside the one before it, so you can see the direction of the last change.

4. **Observations table** — every data point in reverse chronological order (newest first), showing the date and value.

## Find a specific indicator

If you know the FRED series ID (e.g., `FEDFUNDS` for the federal funds rate), you can go directly to `http://localhost:8080/economicdata/FEDFUNDS`. Otherwise, use the [global search](how-to-search.md) — type the indicator name or topic and select the **Economic Indicators** category to filter results.
