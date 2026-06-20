# Ask your AI assistant about portfolio overlap between two funds

Equibles can compare any two institutions' reported 13F portfolios and exposes the comparison through the MCP server, so you can ask your AI assistant "do these two funds own the same stocks, and where do they diverge?" The web portal compares institutions too — an [overlap matrix](how-to-compare-institution-overlap.md) across several filers and a [side-by-side view](how-to-compare-institutions-side-by-side.md) — but only the assistant gives a deep pairwise breakdown in one question.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so both institutions' quarterly 13F holdings have been imported.

## Ask for an overlap

Name two institutions:

- "How much do Berkshire Hathaway and Bridgewater overlap?"
- "Compare the portfolios of Renaissance Technologies and Two Sigma."
- "Where do Citadel and Millennium diverge in their holdings?"

The assistant calls the `GetFundOverlap` tool. It picks the two filers' latest common report date by default — pass a date (YYYY-MM-DD) to compare a specific quarter — and returns up to 30 stocks by default (ask for more or fewer).

## What you should see

A reply with two summary measures plus a side-by-side table. The measures are the Jaccard similarity (the share of stocks the two funds hold in common) and a dollar-weighted overlap (how aligned the portfolios are by position size, not just by name count). The table lists each stock with each fund's shares and the position as a percent of that fund's portfolio, so you can see both what they share and where they diverge.

If the reply says it couldn't compare them, the most likely reasons are that one name didn't match a tracked filer (the tool takes the first match on a partial name — try a more specific name) or the two filers have no 13F report for a common quarter yet. Use large, well-known filers such as Berkshire Hathaway to confirm the data is flowing.
