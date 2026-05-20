# Connect an AI assistant and ask your first question

This tutorial points your AI assistant — Claude Desktop, Claude Code, or ChatGPT Desktop — at the MCP server you started in the previous tutorial, then runs your first real query against your Equibles data.

You'll need about 10 minutes. Pick one assistant and follow that section; you don't need to set up all three.

## Before you start

- The full Equibles stack should be running. If `docker compose ps` shows the `mcp` container as `Up`, you're set. If not, jump back to [Install Equibles with Docker Compose](tutorial-install.md) and come back here.
- The MCP server should be reachable at `http://localhost:8081/mcp`. Quick check: `curl -i http://localhost:8081/mcp` should return some HTTP response (even a 405 Method Not Allowed is fine — it means the endpoint is alive). A connection-refused error means the stack isn't running.
- Pick exactly one assistant from the three sections below. Everything that follows is the same for all three.

## Option A — Claude Desktop

1. Quit Claude Desktop if it's running. The config is only re-read on startup.

2. Open your `claude_desktop_config.json` file. On macOS this lives at `~/Library/Application Support/Claude/claude_desktop_config.json`. On Windows, `%APPDATA%\Claude\claude_desktop_config.json`.

3. If the file doesn't exist yet, create it with this content:

   ```json
   {
     "mcpServers": {
       "equibles": {
         "url": "http://localhost:8081/mcp"
       }
     }
   }
   ```

   If it already exists with other MCP servers, just add the `"equibles": { "url": "..." }` entry inside the existing `mcpServers` block. Save and close the file.

4. Start Claude Desktop. Open a new conversation and look for a small tools / paperclip icon (the exact spot moves between versions, but it's near the input box). You should see Equibles tools listed there — names like `GetTopHolders`, `GetStockPrices`, `SearchEconomicIndicators`. If you see them, the connection is live.

## Option B — Claude Code

Claude Code is the terminal CLI. Adding the MCP server is a one-liner:

1. In any terminal, run:

   ```bash
   claude mcp add equibles --transport http http://localhost:8081/mcp
   ```

   You should see a confirmation that the server was added. The change is picked up by new Claude Code sessions.

2. Start a fresh `claude` session in a project directory. Inside the session, ask: `What MCP tools do I have?` Claude should list Equibles tools alongside any others you have configured.

## Option C — ChatGPT Desktop

1. Quit ChatGPT Desktop if it's running.

2. Open the ChatGPT Desktop config file. On macOS it lives at `~/Library/Application Support/com.openai.chat/mcp.json`. On Windows, `%APPDATA%\com.openai.chat\mcp.json`.

3. If the file doesn't exist yet, create it with this content:

   ```json
   {
     "servers": {
       "equibles": {
         "url": "http://localhost:8081/mcp"
       }
     }
   }
   ```

   If it already exists with other servers, add the `"equibles"` entry inside the existing `servers` block. Save and close.

4. Start ChatGPT Desktop and open a new chat. You should see Equibles tools available alongside any built-in ones.

## Ask your first question

Once your chosen assistant lists Equibles tools, try one of these prompts in a new conversation. Use whichever ticker you've seen show up on the Status page already — `AAPL`, `MSFT`, and `NVDA` are usually the fastest to populate.

- **Holdings:** *"Who are the top ten institutional holders of AAPL right now? Show the position size and the most recent report date."*
- **Prices:** *"Plot AAPL's adjusted close for the last 90 days and tell me the high, low, and median."* (Charts only render in clients that draw them; everywhere else the assistant will return numbers.)
- **Economy:** *"Pull the latest unemployment rate from FRED and tell me how it changed over the past year."* (Only works once you've added a FRED API key — see [Add a FRED API key](how-to-set-up-fred-api-key.md).)
- **Filings:** *"Search Apple's most recent 10-K for the section about supply-chain risk and summarise it."* (Best with the embedding profile enabled — see [Enable semantic search over SEC filings](how-to-enable-embedding-search.md).)

The assistant will call one or more Equibles MCP tools, then summarise the result for you. If a query comes back empty, it usually means Equibles hasn't finished fetching that kind of data yet — give the worker a few more minutes and check the Status page.

## You're done

You've connected an assistant to your self-hosted Equibles stack and run a real query against live data. From here you can hand any new query to the assistant — it picks the right MCP tool and shapes the response.

Next step: browse the [How-to guides](README.md#how-to-guides) for the most common operator tasks (auth, API keys, embeddings, backups, upgrades).
