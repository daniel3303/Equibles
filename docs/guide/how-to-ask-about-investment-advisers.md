# Ask your AI assistant about investment advisers (SEC Form ADV)

Equibles builds a directory of SEC-registered investment advisers from the Form ADV filings it ingests, and exposes it through the MCP server, so you can ask your AI assistant to find an advisory firm by name and then pull its full regulatory profile. To browse the same data on the web portal instead, see [browse investment advisers](how-to-browse-investment-advisers.md).

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so the Form ADV adviser data has been imported.

## Find an adviser by name

Name a firm:

- "Find the investment adviser Renaissance Technologies."
- "Look up Vanguard's advisory firm."
- "Which advisers match 'Bridgewater'?"

The assistant calls the `SearchInvestmentAdvisers` tool and replies with a table of matching firms, largest by regulatory assets under management first. Each row shows the firm name, its **CRD number** (the SEC's unique adviser id), main office location, regulatory assets under management, and employee count.

## Get an adviser's full profile

Once you have a firm's CRD number, ask for its profile:

- "Show me the full Form ADV profile for CRD 231."
- "What are the assets under management and fee structure for that adviser?"

The assistant calls the `GetInvestmentAdviser` tool with the CRD number and replies with the firm's legal and business names, SEC file number, main office and website, regulatory assets under management broken into discretionary, non-discretionary, and total, employee count, and how the firm is compensated (its fee structure).

## What you should see

For a search, a ranked table of advisory firms with their CRD numbers. For a profile, a single firm's complete Form ADV detail. If a search returns nothing, the firm may not be SEC-registered (state advisers and exempt reporting advisers are not in the federal Form ADV set), or its filing may not have been imported yet — try a large, well-known firm such as Vanguard to confirm the data is flowing.
