# Ask your AI assistant about a stock's price history

Equibles imports daily OHLCV (Open, High, Low, Close, Volume) price history from Yahoo Finance and exposes it through the MCP server, so you can ask your AI assistant for a stock's recent prices over a window you choose, or the latest close across a whole watchlist at once. The web portal charts the same prices on a stock profile — see [Explore a company's data on the web portal](tutorial-explore-stock.md) — while the assistant returns the values as a table.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so the stock's daily prices have been imported from Yahoo.

## Ask about prices

Name a stock and the period you want, or list several tickers for a quick check:

- "What's AAPL's price history for the last three months?"
- "Show me Tesla's daily closes so far this year."
- "How did NVDA's stock move between 2024-01-01 and 2024-03-31?"
- "What are the latest prices for AAPL, MSFT, and GOOG?"

For a single stock over a date range the assistant picks `GetStockPrices`; for the most recent close across one or more tickers it picks `GetLatestPrices`. Mention a start and end date or a number of days and the assistant passes them through — otherwise it returns about the last year of daily bars, newest first.

## What you should see

- **`GetStockPrices`** — a table of daily bars with one row per trading day: Date, Open, High, Low, Close, and Volume. Use it for charting, trend questions, or as the basis for the [technical indicators](how-to-ask-about-technical-indicators.md) Equibles computes on top of the same history.
- **`GetLatestPrices`** — the most recent closing price and volume for each ticker you named, ideal for a one-line portfolio or watchlist check.

If the reply says there's no price data, the stock's prices probably haven't been imported yet — confirm the worker has run, then try a large, liquid ticker such as AAPL.
