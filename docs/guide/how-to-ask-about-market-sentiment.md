# Ask your AI assistant about market sentiment (VIX and put/call ratios)

Equibles imports CBOE's volatility and options-sentiment data — the VIX "fear gauge" and put/call ratios — and exposes it through the MCP server, so you can ask your AI assistant how nervous or complacent the market is, and how that has changed over time.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the CBOE scraper has imported the data. It needs no API key — the data comes from CBOE.

## Ask about the VIX

The VIX measures the market's expected 30-day volatility in the S&P 500 — the so-called fear gauge:

- "What has the VIX been doing lately?"
- "Show me VIX history around March 2020."

The assistant calls the `GetVixHistory` tool and replies with daily open/high/low/close values for the period. As a rough guide, readings below 15 signal low volatility or complacency, while readings above 30 signal heightened fear — history is available back to 1990.

## Ask about put/call ratios

Put/call ratios show whether options traders are leaning bearish or bullish:

- "What is the equity put/call ratio telling us right now?"
- "Show me the total put/call ratio trend this year."

The assistant uses `GetPutCallRatios` for the category you ask about — Total, Equity, Index, VIX, or ETP. A ratio above 1.0 leans bearish (more puts than calls); below about 0.7 leans bullish.

## What you should see

A table of daily values labelled by date. These are end-of-day CBOE figures, so the latest row is the most recent completed trading day rather than a live intraday quote.

If the reply says there's no data, the CBOE scraper most likely hasn't run yet.

To browse the same indicators in the browser, see [Browse market indicators (VIX and put/call ratios)](how-to-browse-market-indicators.md).
