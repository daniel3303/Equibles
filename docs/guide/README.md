# User Guide

How to install Equibles, run it, connect AI assistants to it, and handle the most common operator tasks. Pairs with [`../technical/`](../technical/README.md) (codebase / architecture reference for developers).

If you're new, start with the **install tutorial**, then the **connect-an-assistant tutorial**. The how-to guides cover one task each and can be read in any order.

## Tutorials

Walk-throughs for someone seeing Equibles for the first time.

- [Install Equibles with Docker Compose](tutorial-install.md) — from `git clone` to a running stack at `http://localhost:8080`.
- [Connect an AI assistant and ask your first question](tutorial-connect-ai-assistant.md) — point Claude Desktop, Claude Code, or ChatGPT at the MCP server and run a real query.
- [Explore a company's data on the web portal](tutorial-explore-stock.md) — walk through a stock profile's nine data tabs: prices, holdings, short data, financials, filings, and more.

## How-to guides

Task-focused recipes for someone who has Equibles running.

- [Secure the MCP server with an API key](how-to-secure-mcp-api-key.md)
- [Enable login authentication on the web portal](how-to-enable-authentication.md)
- [Add a FRED API key (free economic-indicator data)](how-to-set-up-fred-api-key.md)
- [Add a FINRA API key (free short-volume data)](how-to-set-up-finra-api-key.md)
- [Enable semantic search over SEC filings](how-to-enable-embedding-search.md)
- [Use an existing embedding endpoint (Ollama or OpenAI-compatible)](how-to-use-external-embedding-endpoint.md)
- [Sync only a chosen list of tickers](how-to-restrict-ticker-sync.md)
- [Change how far back data syncs](how-to-change-sync-start-date.md)
- [Upgrade to the latest release](how-to-upgrade.md)
- [Back up and restore your database](how-to-back-up-and-restore.md)
- [Change the log level for troubleshooting](how-to-change-log-level.md)
- [Browse institutional portfolios and run a backtest](how-to-browse-institutions.md)
- [Use the 13F holdings screener](how-to-use-holdings-screener.md)
- [View quarterly holdings activity across the market](how-to-view-holdings-activity.md)
- [Check worker health and recent errors](how-to-view-status-and-errors.md)

## FAQ

Short answers to recurring questions.

- [Frequently asked questions](faq.md) — recurring questions about running Equibles, answered in a few sentences each.
