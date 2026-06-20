# Ask your AI assistant about a company's financial statements

Equibles parses the structured XBRL facts in a company's SEC filings and exposes them through the MCP server, so you can ask your AI assistant for a full financial statement, follow a single metric over time, or compare one figure across competitors — all keyed by ticker.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the financial-statement scraper has imported the XBRL facts. It needs no API key — the data comes from SEC EDGAR.

## Ask for a full statement

Name a company, a statement, and a period:

- "Show me Apple's income statement for fiscal 2024."
- "What did Microsoft's balance sheet look like last fiscal year?"
- "Pull NVDA's cash-flow statement for its latest annual period."

The assistant calls the `GetFinancialStatement` tool and replies with the standard line items — revenue, net income, total assets, operating cash flow, and the rest — for the fiscal year and period you asked for, using the latest restated values.

## Follow one metric over time

To track a single concept across periods rather than a whole statement:

- "How has Apple's revenue changed over the last several years?"
- "Show me Tesla's diluted EPS history."

The assistant uses `GetFinancialFact`, which returns a time series with one row per fiscal period. Common concepts include revenue, net income, diluted EPS, total assets, and operating cash flow.

## Compare companies side by side

To put the same figure next to several peers:

- "Compare net income across AAPL, MSFT, and GOOGL for the latest fiscal year."

The assistant calls `CompareFinancialFact`, returning one row per ticker for the period — any company with no data for that period is listed separately so the comparison stays honest.

## What you should see

A Markdown table (or several) with figures taken directly from the company's XBRL filings, labelled by fiscal year and period. Values are the latest restated numbers unless you ask for them as originally reported.

If a statement or metric is missing, the most likely reasons are that the scraper hasn't imported that company's XBRL facts yet, or the company doesn't tag that concept. For a breakdown of revenue by segment, geography, or product — which lives in dimensional facts rather than the standard statements — see [Ask your AI assistant for a company's revenue breakdown](how-to-ask-about-revenue-breakdown.md).

To read the same statements in the browser, see [View a company's financial statements (SEC XBRL)](how-to-view-financial-statements.md).
