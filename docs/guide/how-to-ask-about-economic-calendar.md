# Ask your AI assistant about the economic release calendar

Equibles tracks when US macro data is published — CPI, the Employment Situation, GDP, and the other indicators it follows — and exposes that calendar through the MCP server, so you can ask your AI assistant what's coming up and what already printed. This calendar is available to AI assistants only; the web portal's Economy page shows the historical *values*, not the schedule.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- The calendar is built from FRED, so you need a FRED API key set and the worker run at least once — see [Add a FRED API key](how-to-set-up-fred-api-key.md).

## Ask what's on the calendar

Ask about a time window, or about a specific release:

- "What economic data is being released this week?"
- "When is the next CPI report?"
- "Show me the macro release calendar for the next 30 days."

The assistant calls the `GetEconomicCalendar` tool and replies with one row per release date, in chronological order. Each row shows the date, the release name (for example, "Employment Situation"), and the FRED series that release updates.

If you don't give dates, the assistant defaults to the next 30 days. Mention a range ("between 2026-01-01 and 2026-03-31") to widen or shift the window — the calendar covers both upcoming scheduled dates and recent past ones.

## Then read the numbers

The calendar tells you *when* a series updates, not its values. Once a release has printed, ask your assistant for the data itself — "what's the latest CPI reading?" — or open the series on the web portal's Economy page (see [Browse economic indicators](how-to-browse-economic-data.md)).

## What you should see

A Markdown table in the assistant's reply, one row per release date. If it comes back empty, either the window has no scheduled releases or the FRED scraper hasn't imported release dates yet — confirm your FRED API key is set and give the worker time to finish its first sync.
