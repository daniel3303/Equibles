# Enable login authentication on the web portal

Turn on a username/password login for the web portal at `http://localhost:8080`. By default the portal is open to anyone who can reach the port — this how-to gates it behind a single shared credential.

1. Open your `.env` file in the project root.

2. Find these two lines (they ship commented out) and uncomment + set them:

   ```env
   AUTH_USERNAME=admin
   AUTH_PASSWORD=use-a-strong-password-here
   ```

   Both must be set. If either is empty, the portal stays open.

3. Restart the web container so it picks up the new variables:

   ```bash
   docker compose up -d --force-recreate web
   ```

4. Visit `http://localhost:8080` in a private/incognito window. You should see a login form. Enter the username and password from step 2. After signing in you'll land on the home page; the session cookie keeps you signed in.

5. The MCP server has a separate API-key gate. To enable that too, set `MCP_API_KEY=<some-long-secret>` in the same `.env`, restart `mcp` (`docker compose up -d --force-recreate mcp`), and have your AI assistant send `Authorization: Bearer <some-long-secret>` with every request. Leaving `MCP_API_KEY` empty keeps the MCP endpoint open.

To turn auth back off, comment out `AUTH_USERNAME` and `AUTH_PASSWORD` (or delete them) and `docker compose up -d --force-recreate web`.
