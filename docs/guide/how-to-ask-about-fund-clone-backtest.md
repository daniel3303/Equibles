# Ask your AI assistant to backtest cloning a fund's portfolio

Equibles can simulate how copying an institutional filer's reported 13F portfolio would have performed against a market benchmark, and exposes the simulation through the MCP server, so you can ask your AI assistant "how would cloning fund X have done versus the market?" The web portal also runs a backtest on each institution's profile page — see [Browse institutional portfolios and run a backtest](how-to-browse-institutions.md) — but only the assistant lets you name any filer and pick the benchmark and window in one question.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so the filer's quarterly 13F holdings and the daily prices the simulation needs have been imported.

## Ask for a clone backtest

Name an institutional filer and, optionally, a benchmark and window:

- "How would cloning Berkshire Hathaway's portfolio have performed against the market?"
- "Backtest cloning CIK 0001067983 versus QQQ over the last 10 years."
- "If I'd copied Bridgewater's 13F portfolio for 5 years, how would I have done versus SPY?"

The assistant calls the `GetFundCloneBacktest` tool. It reconstructs the filer's portfolio at each quarterly 13F snapshot, rebalances on the SEC filing lag so the simulation uses only information available at the time, and values it forward against the benchmark. The benchmark defaults to SPY and the window to 3 years; you can ask for any benchmark ticker and a window from 1 to 20 years.

## What you should see

A reply comparing the cloned portfolio with the benchmark: total return, annualized return (CAGR), and maximum drawdown for each, plus the alpha between them.

A few things shape the result. The simulation only sees holdings as of each 13F filing (quarterly, after the filing lag), so it captures the filer's disclosed long U.S.-equity positions, not intra-quarter trades, options, or short positions. If the reply says it couldn't run, the filer's holdings or the price history for the window probably haven't been imported yet — try a large, long-tenured filer such as Berkshire Hathaway to confirm the data is flowing.
