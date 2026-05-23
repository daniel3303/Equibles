# Frequently asked questions

Short answers to recurring questions about running Equibles. For step-by-step instructions, see the [how-to guides](README.md#how-to-guides).

## How do I disable the "update available" banner on the web portal?

The web portal checks GitHub Releases on a schedule and shows a banner when a newer version is published. To turn the check off, set `CHECK_FOR_UPDATES=false` in your `.env` file (or environment) and restart the `web` service. The banner stays hidden until you flip the setting back to `true`. To actually upgrade when an update is available, see [Upgrade to the latest release](how-to-upgrade.md).

## How much disk space does Equibles need?

Plan for about 5 GB to start, growing over time as scrapers backfill more history. The database (held in the `db-data` Docker volume) is by far the largest consumer; the cached SEC filings are smaller. Pulling the full default range (2020 onwards) for every U.S. ticker is the baseline — restricting to a [chosen list of tickers](how-to-restrict-ticker-sync.md) or [a later sync start date](how-to-change-sync-start-date.md) keeps it much smaller, while extending back to 2000 makes it substantially larger. Enabling the [embedding profile](how-to-enable-embedding-search.md) adds roughly 3 GB more (~2 GB Ollama image, ~1.2 GB BGE-M3 model, plus per-chunk vectors).

## How do I wipe the database and start over?

Run `docker compose down -v` from the project root. The `-v` flag deletes the `db-data` Docker volume along with the containers, so the next `docker compose up` starts with an empty database and the scrapers backfill from scratch using whatever `Worker__MinSyncDate` and `Worker__TickersToSync` are currently set in `.env`. This is destructive — if you want to keep a copy of the current data first, take a snapshot using [Back up and restore your database](how-to-back-up-and-restore.md) before running the command.

## What data sources does Equibles pull from and how often do they update?

Equibles scrapes several public and free-tier data sources automatically. Each scraper runs on its own schedule inside the `worker` container:

| Source | What it provides | Default sync interval |
|--------|-----------------|----------------------|
| **SEC EDGAR** | Company filings (10-K, 10-Q, 8-K, …), financial facts (XBRL), and Failure-to-Deliver data | ~15 seconds (continuous) |
| **SEC EDGAR 13F** | Institutional holdings (who owns what) | Every 24 hours (daily backfill) plus a 6-hour realtime check |
| **Yahoo Finance** | Historical stock prices (OHLCV) | Every 24 hours |
| **FRED** | U.S. economic indicators (GDP, unemployment, CPI, …) | Every 24 hours (requires a [free API key](how-to-set-up-fred-api-key.md)) |
| **FINRA** | Short-sale volume data | Every 24 hours (requires a [free API key](how-to-set-up-finra-api-key.md)) |
| **U.S. Congress** | Congressional stock trades (House disclosures) | Every 12 hours |
| **CBOE** | Options and volatility data | Every 24 hours |
| **CFTC** | Commitments of Traders reports (futures positioning) | Every 24 hours |

The SEC filing and document-processing scrapers run nearly continuously (every 15 seconds) to pick up new filings as they appear on EDGAR. All other scrapers default to a 24-hour cycle. You don't need API keys for SEC, Yahoo, Congress, CBOE, or CFTC — those work out of the box. FRED and FINRA require free API keys; without them those scrapers are simply skipped.

## Can I run Equibles without Docker?

Yes, but Docker is strongly recommended. Equibles requires [ParadeDB](https://www.paradedb.com/) — a PostgreSQL distribution that bundles the `pgvector` and `pg_search` extensions — not a plain PostgreSQL server. The database migrations create BM25 full-text indexes and vector columns that only work when those extensions are installed. If you run against vanilla PostgreSQL, startup will fail with `CREATE EXTENSION "pg_search" does not exist`.

To run without Docker, you would need to: install ParadeDB (or install PostgreSQL with `pgvector` and `pg_search` manually), install the .NET 10 SDK, build the solution with `dotnet build`, and run each host project (`Equibles.Web`, `Equibles.Mcp.Server`, `Equibles.Worker.Host`) separately. The `docker-compose.yml` is the reference for which environment variables each service expects. This path is not officially documented or supported — Docker Compose handles all of this in one command.

## How do I export data from the web portal?

Several pages in the web portal have a **Download CSV** button that exports the data you're currently viewing. Look for it in these places:

| Page | What it exports |
|------|----------------|
| **Stock profile → Holdings tab** | All institutional holders of that stock for the selected report date (holder name, shares, value, percentage). |
| **Institution profile** | The institution's full 13F portfolio for the selected quarter (ticker, shares, value, percentage). |
| **Holdings Screener** | The filtered screener results matching your current query (whatever columns the screener shows). |
| **Holdings Activity** | Quarter-over-quarter position changes (new positions, increased, decreased, sold) for the selected report date. |

The download starts immediately when you click the button — no extra steps. The file is a standard CSV that opens in Excel, Google Sheets, or any spreadsheet tool.

## Can I use Equibles with AI tools other than Claude and ChatGPT?

Yes. Equibles exposes its data through the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/), which is an open standard. Any AI tool that supports connecting to an MCP server over HTTP can use it — you just point the tool at `http://localhost:8081/mcp` (add an `Authorization: Bearer <key>` header if you've [set an API key](how-to-secure-mcp-api-key.md)). The [connect-an-assistant tutorial](tutorial-connect-ai-assistant.md) walks through Claude Desktop, Claude Code, and ChatGPT Desktop specifically, but the same URL and tools work with Cursor, Windsurf, Cline, Continue, and any other MCP-compatible client. Check your tool's documentation for where to add an MCP server URL.

## How do I change the default ports?

Edit the port mappings in `docker-compose.yml`. Each service has a `ports:` entry in `"host:container"` format — change the number on the left (the host port) while leaving the number on the right (the container port) unchanged. The defaults are:

| Service | Default host port | What it serves |
|---------|------------------|----------------|
| `db` | 5432 | PostgreSQL (ParadeDB) |
| `web` | 8080 | Web portal |
| `mcp` | 8081 | MCP server |
| `embedding` | 11434 | Ollama (only with the embedding profile) |

For example, to move the web portal to port 9090, change `"8080:8080"` to `"9090:8080"` under the `web` service, then run `docker compose up -d`. If you change the MCP port, remember to update your AI assistant's config to match (see [Connect an AI assistant](tutorial-connect-ai-assistant.md)).
