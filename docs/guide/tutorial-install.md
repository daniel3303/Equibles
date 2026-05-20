# Install Equibles with Docker Compose

This tutorial walks you from a fresh checkout to a running Equibles stack at `http://localhost:8080`, with worker scrapers pulling in data behind the scenes. You'll need about 15 minutes of attended time, plus an hour or two of unattended time while the first data sync runs.

## What you'll need

- Docker (Engine 20.10+ or Docker Desktop). If `docker --version` and `docker compose version` both print versions, you're set.
- Git. Any recent version works.
- A free email address you're willing to put in the SEC EDGAR User-Agent header. The SEC requires it for programmatic access; they don't sign you up for anything.
- About 5 GB of free disk space for the initial database and cached filings.

You do **not** need .NET, Node.js, or Postgres installed locally — Docker provides everything.

## 1. Clone the repository

Open a terminal and pick a directory you're happy to host the project in. Then run:

```bash
git clone https://github.com/daniel3303/Equibles.git
cd Equibles
```

You should now see a directory listing that includes `docker-compose.yml`, `README.md`, and a `src/` folder.

## 2. Create your `.env` file

Equibles ships with `.env.example` containing every setting you might want to override. Copy it to a real `.env`:

```bash
cp .env.example .env
```

Open `.env` in your editor of choice. You'll see commented-out lines for optional settings (FINRA API key, FRED API key, authentication, etc.). You can leave all of those alone for now. The only setting you **must** change is `SEC_CONTACT_EMAIL`.

## 3. Set your SEC contact email

Find this line in `.env`:

```env
SEC_CONTACT_EMAIL=myname@example.com
```

Replace it with your real email address. Something like:

```env
SEC_CONTACT_EMAIL=alex@example.com
```

The SEC's EDGAR system asks programmatic clients to include a contact email in the User-Agent header. If you skip this step, the SEC scrapers will fail and you'll see errors in the Status page later. Your email is sent only to the SEC; Equibles doesn't store, share, or use it for anything else.

## 4. Start the stack

From the same `Equibles/` directory, run:

```bash
docker compose up
```

The first run takes a few minutes because Docker downloads the ParadeDB database image and builds the three Equibles services. You'll see a wall of build output, then logs from four services scrolling past: `db`, `web`, `mcp`, and `worker`.

When you see lines like this, the stack is ready:

```text
db-1      | LOG:  database system is ready to accept connections
web-1     | info: Microsoft.Hosting.Lifetime[14]
web-1     |       Now listening on: http://[::]:8080
mcp-1     | info: Microsoft.Hosting.Lifetime[14]
mcp-1     |       Now listening on: http://[::]:8080
worker-1  | info: Equibles.Worker.Host.Program[0]
worker-1  |       Holdings scraper running at: 2026-05-20T03:00:00+00:00
```

If you'd rather run the stack in the background, stop it with `Ctrl-C` and run `docker compose up -d` instead. You can always tail logs with `docker compose logs -f`.

## 5. Open the portal

Point your browser at `http://localhost:8080`. You should see the Equibles home page with a navigation bar that includes **Stocks**, **Economic Data**, **Futures**, **Market**, **MCP**, and **Status**.

Click **Status**. The page will show worker counts (initially all zero or near-zero) and an empty error log. This is normal — the scrapers have just started.

## 6. Wait for the first data to arrive

Behind the scenes, the worker is doing several things at once:

- Calling SEC EDGAR for the list of companies, then filings.
- Calling Yahoo Finance for daily stock prices.
- Calling CBOE for VIX and put/call ratio data.
- Calling CFTC for Commitments-of-Traders reports.

Different sources arrive on different schedules. As a rough guide:

- The first stock tickers and basic company info appear within 5 minutes.
- The first prices appear within 10 to 20 minutes.
- Institutional 13F holdings can take an hour or more on the first run because there's a lot of historical data to backfill.

Refresh **Status** occasionally. You'll see the per-domain counts climb. The error log should stay empty; if it doesn't, the most likely cause is a missing or malformed `SEC_CONTACT_EMAIL` — fix it in `.env` and run `docker compose up -d --force-recreate worker` to restart the worker.

## 7. Browse some data

Once **Status** shows non-zero counts for stocks and prices, you can start exploring. Try:

- **Stocks** → search for `AAPL`. Click into the result. You'll see the tab strip (Price, Holdings, Documents, …). Some tabs will be empty until the matching scraper has caught up.
- **Economic Data** → browse FRED indicators by category. (These only appear if you add a free FRED API key — see [How to add a FRED API key](how-to-set-up-fred-api-key.md) when you're ready.)
- **Market** → VIX history and put/call ratios as CBOE catches up.

## You're done

You've got a self-hosted Equibles stack running locally with data scraping live. Everything is plain Docker volumes on your machine — `docker compose down` stops the stack without losing data, `docker compose down -v` wipes it.

Next step: [Connect an AI assistant and ask your first question](tutorial-connect-ai-assistant.md) — point Claude Desktop, Claude Code, or ChatGPT at the MCP server and run a real query against your data.
