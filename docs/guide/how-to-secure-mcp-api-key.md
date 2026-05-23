# Secure the MCP server with an API key

By default the MCP server accepts unauthenticated requests from any client that can reach port 8081. This guide adds a shared secret so only clients that present the correct key can connect.

## Set the key

1. Open your `.env` file in the project root (create one from `.env.example` if you haven't already).

2. Uncomment or add the `MCP_API_KEY` line and set it to a strong, random value:

   ```dotenv
   MCP_API_KEY=my-secret-key
   ```

   Use something long and random in production. A quick way to generate one:

   ```bash
   openssl rand -hex 32
   ```

3. Restart the MCP container so it picks up the new key:

   ```bash
   docker compose restart mcp
   ```

4. Verify that unauthenticated requests are now rejected:

   ```bash
   curl -i http://localhost:8081/mcp
   ```

   You should see a `401 Unauthorized` response with `"API key required"`. That confirms the key is active.

## Update your AI assistant

Every client that was previously connecting without a key now needs to send it as a `Bearer` token in the `Authorization` header. Pick the section that matches your assistant.

### Claude Desktop

1. Quit Claude Desktop.

2. Open `claude_desktop_config.json` (macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`, Windows: `%APPDATA%\Claude\claude_desktop_config.json`).

3. Add a `headers` block to your Equibles entry:

   ```json
   {
     "mcpServers": {
       "equibles": {
         "url": "http://localhost:8081/mcp",
         "headers": {
           "Authorization": "Bearer my-secret-key"
         }
       }
     }
   }
   ```

4. Start Claude Desktop. Tools should appear as before. If they don't, double-check that the key in the config matches the one in `.env` exactly.

### Claude Code

Remove the old server entry and re-add it with the header:

```bash
claude mcp remove equibles
claude mcp add equibles --transport http --header "Authorization: Bearer my-secret-key" http://localhost:8081/mcp
```

Start a new `claude` session and confirm the tools are listed.

### ChatGPT Desktop

1. Quit ChatGPT Desktop.

2. Open `mcp.json` (macOS: `~/Library/Application Support/com.openai.chat/mcp.json`, Windows: `%APPDATA%\com.openai.chat\mcp.json`).

3. Add a `headers` block:

   ```json
   {
     "servers": {
       "equibles": {
         "url": "http://localhost:8081/mcp",
         "headers": {
           "Authorization": "Bearer my-secret-key"
         }
       }
     }
   }
   ```

4. Start ChatGPT Desktop and verify tools load.

## Rotating the key

To change the key later, update `MCP_API_KEY` in `.env`, run `docker compose restart mcp`, and update every client config to match. There is no graceful rollover period — the old key stops working the moment the container restarts, so update clients promptly.
