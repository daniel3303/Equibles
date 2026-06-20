# Ask your AI assistant about a member of Congress's net worth

Equibles reads the annual financial disclosures that members of Congress file and exposes each member's net worth history through the MCP server, so you can ask your AI assistant how a politician's wealth has changed over the years. This data is available to AI assistants only — the web portal shows a member's *trades*, but their net worth is reached through your assistant.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the congressional scraper has imported annual disclosure reports.

## Ask about a member's net worth

Name the member in your question:

- "What is Nancy Pelosi's net worth history?"
- "Show me Marsha Blackburn's net worth over the last 10 years."
- "How has Dan Crenshaw's disclosed net worth changed?"

The assistant calls the `GetMemberNetWorth` tool and replies with one row per filing year, newest first. Each row shows the disclosure year, the date it was filed, the net worth band (minimum and maximum), and how many assets and liabilities the member reported that year.

If the assistant can't match the name, ask it to search first ("find members named Warren") — it uses `SearchCongressMembers` to confirm the exact spelling before looking up the net worth.

## Why every year is a range

Members disclose each asset and liability in a broad dollar band rather than an exact figure, so a year's net worth is also a band: the low end sums the asset minimums and subtracts the liability maximums, and the high end does the reverse. Equibles never invents a single point estimate. A band can be negative when reported liabilities exceed reported assets.

## What you should see

A Markdown table in the assistant's reply, with one row per year. A missing year means the member filed no *electronic* report for it — paper filings are not read — so a gap is not the same as zero net worth. If nothing comes back at all, the scraper may not have imported that member's annual disclosures yet; try a long-serving, well-known member to confirm the data is flowing.

## See also

- [View a member of Congress's trading profile](how-to-view-congress-member-trades.md) — their reported stock trades, on the web portal.
- [Connect an AI assistant and ask your first question](tutorial-connect-ai-assistant.md)
