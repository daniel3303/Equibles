# Ask your AI assistant what institutions are buying and selling (13F leaderboards)

Equibles aggregates every SEC 13F-HR filing each quarter into market-wide leaderboards — the stocks most bought, most sold, most newly initiated, and most exited across all institutional filers versus the prior quarter — and exposes them through the MCP server, so you can ask your AI assistant for the consensus institutional move. For a single fund's holdings see [Ask about an institution's portfolio](how-to-ask-about-institution-portfolio.md); for the current most-held snapshot see [Ask which stocks institutions hold most](how-to-ask-about-most-held-stocks.md); to browse this data on the web portal see [View quarterly holdings activity](how-to-view-holdings-activity.md).

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so 13F-HR filings for at least two consecutive quarters have been imported (the leaderboards compare a quarter against the prior one).

## Ask about the consensus move

Ask what institutions did as a group last quarter:

- "What did institutions buy most last quarter?"
- "Which stocks did funds sell off the most?"
- "What new positions are institutions initiating?"
- "Which stocks did the most funds exit?"

The assistant picks the `GetMarketWide13FActivity` tool and selects the matching leaderboard — top buys, top sells, new positions, or sold-out positions.

## What you should see

A ranked table for the leaderboard you asked about:

- **Top buys** — stocks with the largest increase in institutional holdings, ranked by change in market value.
- **Top sells** — stocks with the largest decrease, ranked by the size of the sell-down.
- **New positions** — stocks ranked by how many filers initiated a position this quarter.
- **Sold-out positions** — stocks ranked by how many filers exited entirely.

If the reply is empty, only one quarter of 13F data has likely been imported so far — there's nothing to compare against yet. Let the worker keep running and try again once a second quarter has loaded.
