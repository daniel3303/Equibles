# Ask your AI assistant for technical indicators

Equibles computes technical indicators on demand from a stock's daily price history and exposes them through the MCP server, so you can ask your AI assistant for a stock's Bollinger Bands, Stochastic Oscillator, Average True Range, or On-Balance Volume as numbers. The web portal draws these indicators on the stock's price chart — see [Explore a company's data on the web portal](tutorial-explore-stock.md) — while the assistant returns the computed values for a window you choose.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so the stock's daily prices have been imported from Yahoo — the indicators are computed from that price history.

## Ask for an indicator

Name a stock and the indicator you want:

- "What are AAPL's Bollinger Bands?"
- "Show me Tesla's Stochastic Oscillator."
- "What's NVDA's Average True Range over the last 14 days?"
- "Give me Microsoft's On-Balance Volume."

The assistant picks the matching tool — `GetBollingerBands`, `GetStochasticOscillator`, `GetAverageTrueRange`, or `GetOnBalanceVolume` — and returns the series. Mention the lookback window (for example "14-day") or the number of points you want, and the assistant passes it through.

## What you should see

A table of values over the recent trading days, with the columns specific to each indicator:

- **Bollinger Bands** — a middle band (a simple moving average of the close) with an upper and a lower band set a number of standard deviations above and below it. Wide bands mean high volatility.
- **Stochastic Oscillator** — %K, which measures the close relative to the high/low range over the lookback window, and %D, its smoothed signal line. Both run 0–100.
- **Average True Range** — Wilder's volatility measure, the average of each bar's true range (how far price moved, allowing for gaps).
- **On-Balance Volume** — a running cumulative volume that adds a bar's volume on up-closes and subtracts it on down-closes, tracking buying versus selling pressure.

If the reply says there's no data, the stock's prices probably haven't been imported yet — confirm the worker has run, then try a large, liquid ticker such as AAPL.
