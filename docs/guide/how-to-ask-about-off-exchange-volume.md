# Ask your AI assistant about off-exchange (dark pool) volume

Equibles imports FINRA's OTC/ATS Transparency data — how much of a stock trades away from the public exchanges, in dark pools and other over-the-counter venues — and exposes it through the MCP server, so you can ask your AI assistant about a stock's dark-pool activity. This data is available to AI assistants only; there is no web-portal page for it.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Off-exchange volume comes from FINRA, so you need a FINRA API key set and the worker run at least once — see [Add a FINRA API key](how-to-set-up-finra-api-key.md).

## Ask about a stock's off-exchange volume

Name a ticker, optionally with a time window:

- "Show me GME's dark pool volume."
- "What has TSLA's off-exchange volume looked like over the last few months?"
- "Give me AAPL's off-exchange volume between 2025-01-01 and 2025-06-30."

The assistant calls the `GetOffExchangeVolume` tool and replies with one row per week. Each week breaks the off-exchange total into its two parts: **ATS** volume and trade count (alternating trading systems — the dark pools) and **non-ATS OTC** volume and trade count (other over-the-counter trading), followed by the combined total.

If you don't give dates, the assistant defaults to the last six months (about 26 weeks). Mention a range to widen or shift the window.

## What this does and doesn't tell you

The FINRA file reports only the off-exchange numbers — it does not include consolidated tape (total market) volume, so the assistant cannot tell you what *share* of a stock's overall volume traded off-exchange. To compute that share, compare these figures against a total-volume source such as the stock's daily price history.

## What you should see

A Markdown table in the assistant's reply, one row per week, oldest to newest. If it comes back empty, either the stock has no reported off-exchange activity in that window or the FINRA scraper hasn't imported it yet — confirm your FINRA API key is set and give the worker time to finish its first sync.
