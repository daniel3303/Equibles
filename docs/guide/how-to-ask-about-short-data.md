# Ask your AI assistant about a stock's short data

Equibles imports short-selling data — daily short volume from FINRA, bi-monthly short interest (including days to cover) from FINRA, and fails-to-deliver records from the SEC — and exposes it through the MCP server, so you can ask your AI assistant how heavily a specific stock is shorted. To browse market-wide short rankings on the web portal, see [Browse market-wide short data](how-to-browse-short-data.md). For squeeze-specific scoring see [Ask for short-squeeze candidates](how-to-ask-about-short-squeeze-candidates.md); for dark-pool volume see [Ask about off-exchange volume](how-to-ask-about-off-exchange-volume.md).

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Short volume and short interest come from FINRA, which needs a free API key — see [Add a FINRA API key](how-to-set-up-finra-api-key.md). Fails-to-deliver comes from the SEC and needs no key.
- Let the worker run after startup so the data has been imported.

## Ask about a stock's short data

Name a stock and what you want to know:

- "What's GME's short interest?"
- "How many days to cover does AMC have?"
- "Show me Tesla's daily short volume over the last month."
- "Are there any fails-to-deliver for SPCE?"

The assistant picks the matching tool — `GetShortInterest`, `GetShortVolume`, or `GetFailsToDeliver` — and returns the series for the date range you mention (or its default window).

## What you should see

- **`GetShortInterest`** — the reported short position, its change from the previous period, average daily volume, and days to cover. Published bi-monthly; a high days-to-cover (above ~5) points to a potential squeeze.
- **`GetShortVolume`** — daily short volume, exempt volume, total volume, and the short-volume percentage. A high percentage (above ~50%) signals heavy intraday short selling.
- **`GetFailsToDeliver`** — SEC settlement records: each settlement date, the quantity of shares that failed to deliver, and the price at settlement.

If the reply says there's no short volume or short interest, the FINRA key probably isn't set or the worker hasn't run yet. Fails-to-deliver is independent of the FINRA key — if that's also empty, confirm the worker has run and try a heavily-shorted ticker such as GME.
