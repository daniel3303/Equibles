# Ask your AI assistant who's buying or selling a stock (top 13F buyers and sellers)

Equibles compares each stock's institutional holdings against the previous quarter's 13F-HR filings and exposes the result through the MCP server, so you can ask your AI assistant which funds moved the needle most on a single stock last quarter. This is the per-stock flow view — for the consensus move across all stocks see [Ask what institutions are buying and selling](how-to-ask-about-market-wide-13f-activity.md), and for the current holder snapshot see [Ask who owns a stock](how-to-ask-about-institutional-ownership.md).

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so 13F-HR filings for at least two consecutive quarters have been imported (the comparison needs a prior quarter).

## Ask who's buying or selling

Name a stock and ask about the quarter's institutional flow:

- "Who's been buying NVDA?"
- "Which funds added the most Tesla last quarter?"
- "Did any institutions sell out of AAPL?"

The assistant picks the `GetTopBuyersSellers` tool and returns the result for the stock.

## What you should see

A table with two sections for the quarter:

- **Top buyers** — the institutions with the biggest absolute increase in shares held versus the prior 13F report, including funds that opened a brand-new position.
- **Top sellers** — the institutions with the biggest absolute reduction, including funds that exited the position entirely.

If the reply is empty, only one quarter of 13F data has likely been imported, so there's no prior quarter to compare against — let the worker keep running and try again once a second quarter has loaded.
