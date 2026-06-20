# Ask your AI assistant who owns a stock (institutional holders)

Equibles tracks the quarterly 13F-HR holdings that large investment managers file with the SEC and exposes them through the MCP server, so you can ask your AI assistant which institutions own a stock, how that ownership has shifted over time, and who bought or sold the most last quarter — all keyed by ticker.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the 13F scraper has imported the holdings. It needs no API key — the data comes from SEC EDGAR.

## Ask who holds a stock

Name a company and ask about its institutional ownership:

- "Who are the top institutional holders of AAPL?"
- "Which funds own the most Microsoft, and what is each stake worth?"

The assistant calls the `GetTopHolders` tool and replies with a ranked table of institutions by shares held, each with its market value and its share of total institutional ownership, for the latest 13F report date. Ask for a specific date if you want an earlier quarter.

## Track ownership over time

To see whether institutional interest is rising or falling:

- "Has institutional ownership of NVDA been growing or shrinking?"
- "Show me Tesla's institutional ownership trend over the last few quarters."

The assistant uses `GetOwnershipHistory`, which reports the total institutional shares, market value, and number of holders across recent quarters.

## See who bought and sold last quarter

For the most actionable quarterly signal:

- "Which institutions added or cut the most AAPL last quarter?"

The assistant calls `GetTopBuyersSellers` and returns two sections — the biggest share additions (including brand-new positions) and the biggest reductions (including positions sold out entirely) versus the previous 13F report date.

## What you should see

One or more ranked Markdown tables in the assistant's reply, labelled with the 13F report date the figures come from. Because 13F filings are quarterly and due up to 45 days after quarter-end, the latest available date is usually the most recent completed quarter rather than today.

If the reply says there's no data, the 13F scraper most likely hasn't imported that stock's holdings yet — a widely-held large-cap such as AAPL or MSFT is the best place to confirm the data is flowing.

To browse the same data from an institution's point of view in the browser, see [Browse institutional portfolios and run a backtest](how-to-browse-institutions.md).
