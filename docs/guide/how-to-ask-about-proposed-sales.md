# Ask your AI assistant about proposed insider sales (Form 144)

Equibles imports SEC Form 144 notices — an affiliate's declaration of intent to sell restricted or control shares — and exposes them through the MCP server, so you can ask your AI assistant about a company's upcoming insider selling before it executes. Form 144 is the forward-looking complement to executed Form 4 trades — to ask about those, see [Ask about a company's insider trading](how-to-ask-about-insider-trading.md). To browse the same notices on the web portal, see [View proposed insider sales](how-to-view-proposed-sales.md).

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so Form 144 notices have been imported from SEC EDGAR.

## Ask about proposed sales

Name a stock and ask about intended insider selling:

- "What proposed insider sales are there for AAPL?"
- "Has anyone at Tesla filed to sell shares recently?"
- "Show me NVDA's Form 144 filings."

The assistant picks the `GetProposedSales` tool and returns the company's recent Form 144 notices.

## What you should see

A table of recent Form 144 notices, each showing the seller, their relationship to the company, the number of shares and aggregate market value to be sold, the approximate sale date, and the broker.

Remember that a Form 144 is a *declaration of intent*, not a completed trade — the actual sale, if it happens, later appears as an executed [Form 4 insider transaction](how-to-ask-about-insider-trading.md).

If the reply says there are no proposed sales, the company may simply have no recent Form 144 filings — confirm the worker has run, then try a large, actively-traded ticker such as AAPL.
