# Ask your AI assistant about a company's insider trading

Equibles tracks the Form 3, Form 4, and Form 144 filings that company insiders — directors, officers, and 10%-or-more owners — submit to the SEC, and exposes them through the MCP server, so you can ask your AI assistant who is buying or selling a stock, what each insider owns, and which sales are coming up.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the insider scraper has imported the filings. It needs no API key — the data comes from SEC EDGAR.

## Ask what insiders are trading

Name a company and ask about its insider activity:

- "What insider buying and selling has there been at AAPL recently?"
- "Have any Tesla executives sold shares in the last few months?"

The assistant calls the `GetInsiderTransactions` tool and replies with recent purchases, sales, and awards from Form 3 and Form 4 filings — each with the insider's name and role, the transaction type, the number of shares and price, and the holding left afterward.

## Ask who owns what

To see the insider ownership structure rather than individual trades:

- "Who are the insiders at Microsoft and how much does each hold?"

The assistant uses `GetInsiderOwnership`, which lists each insider, their role, total shares held, and their most recent transaction.

## Ask about upcoming sales

A Form 144 is an affiliate's declared intent to sell, filed before the trade — an early signal that often precedes an executed Form 4:

- "Are there any proposed insider sales pending at NVDA?"

The assistant calls `GetProposedSales` and returns each Form 144 notice — the seller and their relationship to the company, the shares and aggregate market value to be sold, the approximate sale date, and the broker.

## Look up a specific insider

- "Find the insider named Cook." — the assistant uses `SearchInsiders` to match insiders by name across every company they have filed for.

## What you should see

A reply listing the matching transactions, holders, or proposed sales, drawn directly from the SEC filings — Equibles reports the figures exactly as filed.

If the reply says there's no data, the insider scraper most likely hasn't imported that company's filings yet. A large company with active insiders such as AAPL or MSFT is the best place to confirm the data is flowing.

To browse the same data in the browser instead, see [View market-wide insider trading activity](how-to-view-insider-activity.md) and [View an insider's trading profile](how-to-view-insider-profile.md).
