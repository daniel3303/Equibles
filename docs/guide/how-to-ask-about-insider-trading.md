# Ask your AI assistant about a company's insider trading

Equibles tracks the Form 3, Form 4, Form 5, and Form 144 filings that company insiders — directors, officers, and 10%-or-more owners — submit to the SEC, and exposes them through the MCP server, so you can ask your AI assistant who is buying or selling a stock, what each insider owns, and which sales have been proposed.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the insider scraper has imported the filings. It needs no API key — the data comes from SEC EDGAR.

## Ask what insiders are trading

Name a company and ask about its insider activity:

- "What insider buying and selling has there been at AAPL recently?"
- "Have any Tesla executives sold shares in the last few months?"

The assistant calls the `GetInsiderTransactions` tool and replies with recent purchases, sales, and awards from Forms 4 and 5 — each with the insider's name and role, the transaction type, the number of shares and price, and the holding left afterward. Form 3 supplies initial ownership rather than a transaction.

## Ask who owns what

To see the insider ownership structure rather than individual trades:

- "Who are the insiders at Microsoft and how much does each hold?"

The assistant uses `GetInsiderOwnership`, which lists each insider, their role, total shares held, and their most recent transaction.

## Ask about proposed sales

A Form 144 is an affiliate's declared intent to sell, not evidence that a trade will occur. A completed sale may later appear on Form 4 or 5 when it is reportable there:

- "Are there any proposed insider sales pending at NVDA?"

The assistant calls `GetForm144ProposedSales` and returns each Form 144 notice — the seller and their relationship to the company, the shares and aggregate market value to be sold, the sale as a percent of shares outstanding, the approximate sale date, the broker, and filed remarks such as a stated 10b5-1 plan.

## Look up a specific insider

- "Find the insider named Cook." — `SearchInsiders` first requires every whole query word in the SEC-filed legal name, independent of punctuation or order, then broadens to any whole word only when no strict row matches. A token inside a different word is not a match; verified public-name aliases such as Jensen Huang are recognized.

## What you should see

A reply listing the matching transactions, holders, or proposed sales, drawn directly from the SEC filings — Equibles reports the figures exactly as filed.

If the reply says there's no data, the insider scraper most likely hasn't imported that company's filings yet. A large company with active insiders such as AAPL or MSFT is the best place to confirm the data is flowing.

To browse the same data in the browser instead, see [View market-wide insider trading activity](how-to-view-insider-activity.md) and [View an insider's trading profile](how-to-view-insider-profile.md).
