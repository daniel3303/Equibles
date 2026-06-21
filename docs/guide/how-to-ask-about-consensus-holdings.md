# Ask your AI assistant what several funds agree on (consensus holdings)

Equibles can pool the latest 13F portfolios of several institutions into one consensus view and exposes it through the MCP server, so you can ask your AI assistant "what do these funds agree on?" or "what are the top picks across these investors combined?" Unlike [portfolio overlap](how-to-ask-about-fund-overlap.md), which compares exactly two funds, this works for a whole group at once. The web portal has the same view — see [view the combined portfolio of several institutions](how-to-view-combined-institution-portfolio.md) — but the assistant answers it from a single question.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so each institution's quarterly 13F holdings have been imported.

## Ask for the consensus

Name two to twenty-five institutions:

- "What do Berkshire Hathaway, Bridgewater, and Renaissance Technologies agree on?"
- "Show me the top picks shared by Citadel, Millennium, and Two Sigma."
- "Which stocks are held by at least three of Tiger Global, Coatue, Lone Pine, and Viking?"

The assistant calls the `GetConsensusHoldings` tool. It picks the institutions' latest common report date by default — pass a date (YYYY-MM-DD) to use a specific quarter — and returns up to 30 stocks by default (ask for more or fewer). You can also ask it to show only stocks held by at least a given number of the funds.

## What you should see

A reply with a table of stocks ranked by how many of the named funds hold each one (most widely held first), then by the combined dollar value across those funds. Each row shows how many of the group own the stock and the group's total position, so the strongest consensus picks sit at the top and the one-fund outliers fall to the bottom.

If the reply says it couldn't build a consensus, the most likely reasons are that a name didn't match a tracked filer (the tool takes the first match on a partial name — try a more specific name) or the funds have no 13F report for a common quarter yet. Use large, well-known filers such as Berkshire Hathaway to confirm the data is flowing. To compare just two funds in depth instead, see [ask about portfolio overlap between two funds](how-to-ask-about-fund-overlap.md).
