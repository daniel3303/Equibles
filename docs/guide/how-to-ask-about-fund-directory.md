# Ask your AI assistant about funds in the directory

Equibles builds a directory of registered investment companies (mutual funds and ETFs) from the SEC Form N-PORT-P reports it ingests, and exposes it through the MCP server. You can ask your AI assistant to find a fund by name — even one of the big multi-series trusts that has no ticker of its own — and then pull that fund's profile and largest holdings. This directory is available to AI assistants only; the web portal shows fund disclosures per stock (on a company's profile), not a searchable fund directory.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the fund directory has been built from imported N-PORT-P reports. It needs no API key — the data comes from SEC EDGAR.

## Find a fund

Ask your assistant to search by fund name, registrant, or ticker:

- "Find the iShares Russell 2000 ETF."
- "Search the fund directory for Vanguard."
- "What fund has the ticker VOO?"

The assistant calls the `SearchFunds` tool and replies with a table of matching fund series, largest by net assets first. Each row shows the fund's name, its **profile id**, ticker (when the fund is itself listed), fund type, net assets, number of reported holdings, and the latest report date.

The profile id is the key to the next step — note it for the fund you care about. The big fund families (iShares, Vanguard, Fidelity) run many series under one registrant and often have no single ticker, so searching by name or registrant is the way to reach them.

## View a fund's profile and holdings

Ask for one fund's details, using either its profile id from the search or its ticker:

- "Show me the profile for ishares-russell-2000-etf-s000002277."
- "What are VOO's largest holdings?"
- "How much does the Fidelity Contrafund hold, and in what?"

The assistant calls the `GetFundProfile` tool and replies with the fund's registrant and series, its latest reporting period, net and total assets, and a table of its largest holdings — issuer name, CUSIP, position size, U.S.-dollar value, share of net assets, and asset category, largest first.

For the large multi-series trusts, only positions in tracked stocks are stored, so the holdings table shows the fund's tracked-stock positions while the net-asset totals are the fund's real totals.

## What you should see

A Markdown table in the assistant's reply for each step. If nothing comes back, the most likely reasons are that the worker hasn't built the directory yet, or the search term is too specific — try a broader term (for example a fund family like "iShares" or "Vanguard") to confirm the directory is populated, then drill in with the profile id it returns.
