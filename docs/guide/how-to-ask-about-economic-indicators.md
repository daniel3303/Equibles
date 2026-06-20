# Ask your AI assistant about economic indicators (FRED data)

Equibles pulls macroeconomic series from the Federal Reserve's FRED database — interest rates, inflation, employment, GDP, yield spreads, and more — and exposes them through the MCP server, so you can ask your AI assistant for the value or trend of any tracked indicator.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- FRED data needs a free API key — see [Add a FRED API key](how-to-set-up-fred-api-key.md). Once it is set, let the worker run for a while so the economic scraper imports the series.

## Ask for one indicator over time

Name an indicator and ask for its history:

- "What has the unemployment rate done over the past year?"
- "Show me CPI inflation since 2020."
- "How has the 10-year/2-year yield spread moved recently?"

The assistant calls the `GetEconomicIndicator` tool and replies with the time series of observations. Common series include the fed funds rate, CPI, unemployment, GDP, the 10Y/2Y yield spread, and the 30-year mortgage rate. If it is unsure of the exact series, it uses `SearchEconomicIndicators` to look it up by name.

## Ask for a macro snapshot

To see where the economy stands across the board:

- "Give me a snapshot of current macro conditions."
- "What are the latest readings on rates, inflation, and employment?"

The assistant uses `GetLatestEconomicData`, which returns the most recent value for key indicators grouped by category — interest rates, yield spreads, inflation, employment, GDP, money supply, sentiment, housing, exchange rates, and market indicators.

## What you should see

A table of dated observations for each series, taken straight from FRED. Economic data is revised, so the latest value for a series may change as the source publishes revisions.

If the reply says there's no data, the most likely reason is that the FRED API key is not set yet — see the link above — or the scraper has not imported that series.

To see when the next releases are scheduled, see [Ask your AI assistant about the economic release calendar](how-to-ask-about-economic-calendar.md). To browse the same indicators in the browser, see [Browse economic indicators](how-to-browse-economic-data.md).
