# Ask your AI assistant which funds and ETFs hold a stock

Equibles tracks the portfolio holdings that registered investment companies (mutual funds and ETFs) report on SEC Form N-PORT-P, and exposes them through the MCP server, so you can ask your AI assistant "which funds and ETFs own this stock?" This is the reverse of looking up a single fund's holdings — instead of starting from a fund, you start from a stock and get back every fund series that reports a current position in it. It complements [institutional (13F) ownership](how-to-ask-about-institutional-ownership.md), which covers hedge funds and asset managers rather than registered funds.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so fund N-PORT-P reports have been imported.

## Ask which funds hold a stock

Name a ticker:

- "Which funds and ETFs hold AAPL?"
- "What mutual funds own the most Microsoft?"
- "Show me the funds with the biggest positions in NVDA."

The assistant calls the `GetFundsHoldingStock` tool. It matches the stock's CUSIP against the holding rows on each fund series' most recent N-PORT-P report, so an exited position never shows as current, and returns up to 20 fund positions by default (ask for more or fewer), largest by U.S.-dollar value first.

## What you should see

A reply with a table of the funds holding the stock, one row per fund series. Each row shows the registrant and series name, the report date, the position size (balance and units), its U.S.-dollar value, its share of the fund's net assets, and the payoff profile (Long or Short). The largest positions come first, so the funds with the most exposure are at the top.

If the reply says it can't resolve fund ownership, the most likely reasons are that no CUSIP is on record for the ticker yet, or no fund reports a position in it on its most recent N-PORT-P. Use a large, widely-held stock such as AAPL to confirm the data is flowing. To go the other way — start from a fund and list what it holds — see [ask about funds in the directory](how-to-ask-about-fund-directory.md) or, on the web portal, [view a fund's portfolio holdings](how-to-view-fund-holdings.md).
