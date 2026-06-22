# Ask your AI assistant which stocks institutions hold most

Equibles ranks every stock by how widely institutions hold it across their SEC Form 13F filings, and exposes the ranking through the MCP server, so you can ask your AI assistant which stocks institutions are crowding into — and whether that crowd is growing or shrinking. This market-wide ranking is available to AI assistants only; the web portal shows holdings per stock and per institution, not a single breadth leaderboard.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so 13F filings have been imported for at least one quarter (two quarters for the quarter-over-quarter view).

## Ask for the ranking

Ask a market-wide question:

- "Which stocks are held by the most institutions right now?"
- "Show me the stocks with the biggest jump in institutional ownership this quarter."
- "Rank stocks by total institutional dollar value held."

The assistant calls the `GetMostHeldStocks` tool. By default it ranks by the number of 13F filers reporting each stock for the latest available quarter; ask for the quarter-over-quarter change in filer count (a warming / cooling view) or for total reported dollar value instead. Pass a report date (YYYY-MM-DD) to pin a specific quarter, and ask for more or fewer than the default 25 stocks.

## What you should see

A Markdown table of stocks for the chosen quarter. Each row shows the number of 13F filers holding the stock, the change in filer count versus the prior quarter, the total reported value and its change, and the stock's share of the whole 13F universe — so you can see both how crowded a stock is and whether institutions are still piling in or backing out.

If the reply is empty or the change columns are blank, only one quarter of 13F data has been imported so far — let the worker keep running, then ask again once a second quarter is on file.
