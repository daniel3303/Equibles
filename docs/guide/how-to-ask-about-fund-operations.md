# Ask your AI assistant about a fund's operations (SEC Form N-CEN)

Equibles ingests the SEC Form N-CEN annual reports that registered funds file and exposes them through the MCP server, so you can ask your AI assistant who runs and services a mutual fund, ETF, or closed-end fund — its investment advisers, custodians, transfer agents, auditors, and more. This is the operational side of a fund; to ask what a fund *owns* instead, see [ask about funds in the directory](how-to-ask-about-fund-directory.md). To browse the same data on the web portal, see [view a fund's operations](how-to-view-fund-operations.md).

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so fund N-CEN reports have been imported.

## Ask about a fund's operations

Name a fund by its ticker:

- "Who is the custodian and auditor for SPY?"
- "What service providers does VOO use?"
- "Show me the operational filings for the Mexico Fund (MXF)."

The assistant calls the `GetFundOperations` tool with the fund's ticker and returns up to 10 annual reports by default (ask for more or fewer), newest first.

## What you should see

For each N-CEN report, you see the fund's classification (for example N-1A open-end or N-2 closed-end), Investment Company Act file number, reporting period, and first/last-filing flags. The answer then separates the newest filing's named service providers from an exact filed-name timeline across the returned reports, so a changed or omitted adviser, custodian, transfer agent, administrator, auditor, or underwriter remains visible.

If the reply comes back empty, the ticker may not belong to a registered fund — only mutual funds, ETFs, and closed-end funds file N-CEN, so an operating company returns no data — or its N-CEN may not have been imported yet. Try a large, well-known fund such as SPY to confirm the data is flowing.
