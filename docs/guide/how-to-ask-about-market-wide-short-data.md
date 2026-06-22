# Ask your AI assistant about market-wide short data

Equibles aggregates FINRA short data across every tracked stock and exposes it through the MCP server, so you can ask your AI assistant where short selling was most concentrated on a given day or which stocks carry the highest short interest right now — without naming a ticker. For a single company's short data see [ask about a stock's short data](how-to-ask-about-short-data.md); to browse the same leaderboards on the web portal see [browse market-wide short data](how-to-browse-short-data.md).

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Add a FINRA API key and let the worker run so short-volume and short-interest data have been imported — see [add a FINRA API key](how-to-set-up-finra-api-key.md).

## Ask for the largest daily short volume

Ask which stocks were most shorted on a day:

- "Which stocks had the largest short volume today?"
- "Show me the most-shorted stocks on 2025-03-14."
- "What were the top short-volume names yesterday, above 5 million shares?"

The assistant calls the `GetLargestShortVolume` tool. It uses the latest available trading day by default — pass a date (YYYY-MM-DD) for a specific day — returns up to 50 stocks by default (ask for more or fewer), and can filter out names below a minimum short volume. Results are sorted by short volume, largest first.

## Ask for the highest short interest

Ask which stocks carry the most short interest across the market:

- "Which stocks have the highest short interest right now?"
- "Show me the names with the most days to cover."
- "List stocks with at least 10 days to cover."

The assistant calls the `GetShortInterestSnapshot` tool. It returns the latest short-interest figures across all stocks, sorted by days to cover (highest first), up to 50 by default, and can filter to a minimum days-to-cover. A high days-to-cover means it would take many days of normal trading volume to buy back all the shorted shares — one of the signals behind a short squeeze.

## What you should see

For short volume, a table of stocks ranked by that day's short volume. For short interest, a table ranked by days to cover. If a reply comes back empty, FINRA data may not be imported yet (the FINRA scraper needs an API key), or the requested day may have no data — try the latest day with no date. For a composite, ranked short-squeeze signal rather than the raw days-to-cover list, see [ask for short-squeeze candidates](how-to-ask-about-short-squeeze-candidates.md).
