# Ask your AI assistant about futures positioning (CFTC Commitments of Traders)

Equibles imports the CFTC's weekly Commitments of Traders (COT) report — how commercial hedgers and non-commercial speculators are positioned across dozens of futures contracts — and exposes it through the MCP server, so you can ask your AI assistant how traders are positioned in a market and how that has shifted.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the CFTC scraper has imported the COT reports. It needs no API key — the data comes from the CFTC.

## Ask how a market is positioned

Name a futures market and ask about positioning:

- "How are traders positioned in crude oil futures?"
- "Show me the COT positioning trend for gold."

The assistant calls the `GetCftcPositioning` tool and replies with commercial and non-commercial positions for that contract over time. If it needs the exact contract first, it uses `SearchCftcMarkets` to find the market code by name.

## Ask for a market-wide snapshot

To see positioning across everything at once:

- "Give me the latest COT snapshot across all futures."
- "Where are speculators most net-long right now?"

The assistant uses `GetLatestCftcData`, which returns the latest positioning grouped by category — Agriculture, Energy, Metals, Equity Indices, Interest Rates, and Currencies — with commercial and non-commercial net positions.

## What you should see

A table of positions per contract, labelled with the report date. The COT report is published weekly, with positions reported as of the prior Tuesday, so the latest snapshot reflects the most recent published week rather than today.

If the reply says there's no data, the CFTC scraper most likely hasn't run yet, or the market name didn't match — ask the assistant to search for the contract first.

To browse the same positioning data in the browser, see [Browse futures positioning (CFTC Commitments of Traders)](how-to-browse-futures.md).
