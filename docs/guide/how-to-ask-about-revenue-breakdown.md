# Ask your AI assistant for a company's revenue breakdown

Equibles reads the dimensional XBRL facts a company tags in its own SEC filings and exposes them through the MCP server, so you can ask your AI assistant how a company's revenue splits by business segment, geography, and product or service. This breakdown is available to AI assistants only; the web portal shows a company's headline financial statements per stock, not the disaggregated revenue tables.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the financial-statement scraper has imported the company's XBRL facts. It needs no API key — the data comes from SEC EDGAR.

## Ask for a breakdown

Name a company and ask how its revenue splits:

- "Break down Apple's revenue by segment, geography, and product."
- "How does Microsoft's revenue split across business segments over the last few years?"
- "Show me NVDA's revenue by product and service."

The assistant calls the `GetRevenueBreakdown` tool and replies with one table per axis the company actually reports — by segment, by geography, and by product and service. Each table lists annual fiscal years across the top and the company's own reported categories down the side, using the latest restated values. By default it covers the last 8 fiscal years; ask for fewer or more (up to 12) if you want a different window.

## What you should see

One or more Markdown tables in the assistant's reply, one per axis the company discloses. The figures are exactly as the company reported them — never estimated or allocated by Equibles — and annual only (quarterly slices are not included).

If a table is missing or the reply says there's no breakdown, the most likely reasons are that the company doesn't disaggregate revenue on that axis in its filings, or the scraper hasn't imported its XBRL facts yet. Companies vary widely in how much they break out: a large, diversified issuer (for example AAPL or MSFT) is the best place to confirm the data is flowing.
