# Ask your AI assistant about an institution's portfolio

Equibles tracks the quarterly 13F-HR filings of institutional investors (fund managers) and exposes them through the MCP server, so you can ask your AI assistant what a specific fund holds, how large and concentrated it is, how it splits across sectors, and what it bought or sold last quarter. To browse the same data on the web portal instead, see [Browse institutional portfolios and run a backtest](how-to-browse-institutions.md). To ask which institutions own a particular stock, see [Ask your AI assistant who owns a stock](how-to-ask-about-institutional-ownership.md).

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so 13F-HR filings have been imported from SEC EDGAR.

## Ask about a fund

Name the fund and what you want to know:

- "What does Berkshire Hathaway hold?"
- "How big and concentrated is Bridgewater's portfolio?"
- "What's Renaissance Technologies' sector allocation?"
- "What did Citadel buy and sell last quarter?"

If the name is ambiguous, the assistant first calls `SearchInstitutions`. Discovery tries all name tokens first and broadens to any token only when no strict row matches; its rows include SEC CIK, latest report date, reported 13F AUM, tracked position count, and location. Separate CIKs can represent current, predecessor, or otherwise distinct registrants, so compare the dates and use the intended CIK. Institution-scoped tools never use the broad fallback: they accept a unique strict partial or verified flagship alias, but reject ambiguity instead of selecting silently.

## What you should see

- **`GetInstitutionPortfolio`** — the fund's holdings: each tracked stock with share count and market value.
- **`GetInstitutionSummary`** — a header with reported AUM, position count, top-10 / top-25 concentration, quarter-over-quarter turnover, and the latest and prior report dates. Good for "how big and how concentrated is this fund?"
- **`GetInstitutionSectorAllocation`** — holdings grouped by industry/sector and sorted by percentage of the portfolio. Good for "is this fund a tech specialist or a generalist?"
- **`GetInstitutionQuarterlyActivity`** — the stocks the fund initiated, increased, reduced, or exited versus the prior quarter, sorted by the size of the change.

If the reply says the institution wasn't found, try a fuller or more distinctive part of its name, or run `SearchInstitutions` first to confirm how it's listed.
