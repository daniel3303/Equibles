# Ask your AI assistant about congressional securities transactions

Equibles collects the periodic transaction reports that members of the U.S. House and Senate file when they transact in securities, and exposes them through the MCP server. You can ask which members traded a company's stock, option, bond, or another reported instrument, or what a particular member has been trading.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the congressional-disclosure scraper has imported the reports. It needs no API key — the data comes from the House and Senate disclosure portals.

## Ask who in Congress traded a stock

Name a company and ask which members traded it:

- "Which members of Congress have traded NVDA?"
- "Has anyone in Congress bought or sold Apple recently?"

The assistant calls the `GetCongressionalTrades` tool and replies with each disclosed transaction tied to that ticker — the member, transaction type, filed Asset instrument, date, and disclosed amount. Because disclosure rules report a dollar range rather than an exact figure, amounts appear as bands.

## Ask what a member has been trading

To follow one member instead of one stock:

- "What has Nancy Pelosi been trading?"
- "Show me Tommy Tuberville's recent securities transactions."

The assistant uses `GetMemberTrades` for that member, listing each transaction with its ticker, filed Asset instrument, transaction type, and amount. `SearchCongressMembers` tries all name tokens first, broadens to any token only when no strict row matches, and recognizes verified public-name aliases such as Dan Crenshaw → Daniel Crenshaw.

## What you should see

A list of securities transactions drawn straight from the official House and Senate filings, each tagged with the filed Asset instrument plus transaction and filing dates. Disclosures are filed after the transaction — often weeks later — and amounts are ranges, so treat the figures as the bands Congress reports rather than exact values.

If the reply says there's no data, either the member or company has no disclosed trades on record, or the scraper hasn't imported the latest filings yet.

To browse a single member's trading profile in the browser, see [View a member of Congress's trading profile](how-to-view-congress-member-trades.md). For their net worth, see [Ask your AI assistant about a member of Congress's net worth](how-to-ask-about-congress-member-net-worth.md).
