# Ask your AI assistant about federal government contracts

Equibles tracks federal contract awards from [USAspending.gov](https://www.usaspending.gov/) and exposes them through the MCP server, so you can ask your AI assistant which public companies win government business and how much. This data is available to AI assistants only — there is no web-portal page for it yet.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the USAspending scraper has imported award data. It syncs once every 24 hours and needs no API key.

## Look up one company's contracts

Ask your assistant about a specific ticker:

- "What federal contracts has LMT won in the last year?"
- "Show me Boeing's government contract awards since 2024-01-01."
- "How much has RTX been awarded recently, and by which agencies?"

The assistant calls the `GetGovernmentContracts` tool and replies with a table of awards. Each row shows the action date, awarding agency, award type, dollar amount, award ID, and a short description, ordered largest first — useful for gauging how much a company relies on federal spending.

To narrow the window, mention dates ("between 2024-01-01 and 2024-12-31"). When you don't, the assistant defaults to the last year.

## Rank the biggest federal contractors

Ask a market-wide question instead of naming a company:

- "Which public companies won the most federal contract dollars last quarter?"
- "Rank the top 10 government contractors in 2024."

The assistant calls the `GetTopGovernmentContractors` tool and replies with public companies ranked by total federal dollars awarded over the date range you give (or the last year by default).

## What you should see

A Markdown table in the assistant's reply. If nothing comes back, the most likely reasons are that the scraper hasn't imported data for that company yet, or the ticker isn't tracked — ask about a large, well-known federal contractor (for example LMT, RTX, or BA) to confirm the data is flowing.
