# Ask your AI assistant about upcoming FDA catalysts

Equibles tracks the [FDA advisory-committee (AdComm) calendar](https://www.fda.gov/advisory-committees/advisory-committee-calendar) — the scheduled panel-meeting dates that often move biotech and pharma stocks — and exposes it through the MCP server. You can ask your AI assistant what regulatory catalysts are coming up. This data is available to AI assistants only — there is no web-portal page for it yet.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the FDA scraper has imported the calendar. It syncs once every 24 hours and needs no API key.

## See what's coming up

Ask your assistant about scheduled meetings:

- "What FDA advisory-committee meetings are coming up in the next 90 days?"
- "Are there any FDA catalysts scheduled between 2024-09-01 and 2024-12-31?"
- "List the next 20 FDA AdComm meetings."

The assistant calls the `GetFdaCatalysts` tool and replies with a table of meetings, soonest first. Each row shows the meeting date, the meeting title, the FDA center holding it, the catalyst type, and an end date for multi-day meetings.

When you don't give dates, the assistant looks at the next 90 days starting today. Mention a date range to look further ahead or back.

## What you should see

A Markdown table in the assistant's reply, ordered by date. If nothing comes back, either there are no meetings scheduled in that window or the scraper hasn't imported the calendar yet — widen the range (for example, "in the next 6 months") to confirm the data is flowing.
