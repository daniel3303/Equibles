# Operations

Running and maintaining a self-hosted Equibles deployment. Configuration catalog, compose profiles, upgrading, monitoring, and storage.

## Configuration loading

- Every host (Web, MCP, Worker) reads configuration in this order, later sources overriding earlier ones: `appsettings.json` → `appsettings.<Env>.json` → environment variables.
- `docker-compose.yml` passes the values from your `.env` file (via `${VAR:-default}` substitution) into each service as environment variables — the typical operator never edits `appsettings.json` directly.
- Nested settings use the double-underscore form: `Sec__ContactEmail` becomes `Sec:ContactEmail` in the .NET config tree; array entries use a numeric segment: `Worker__TickersToSync__0`.

## Required

| Variable | Why | Where it's read |
|---|---|---|
| `SEC_CONTACT_EMAIL` | SEC EDGAR's fair-access policy requires a contact email in the User-Agent header. Without it, EDGAR requests get rate-limited or blocked entirely. | Web + Worker hosts (`Sec__ContactEmail`). |

`docker compose up` will start without it, but every SEC scraper cycle will fail and log errors. Set it before the first deploy.

## Optional API keys

| Variable | Source | Effect when unset |
|---|---|---|
| `Finra__ClientId` + `Finra__ClientSecret` | [FINRA API Console](https://gateway.finra.org/app/api-console) | The FINRA short-volume / short-interest scrapers skip gracefully. Fails-to-deliver still works (it's SEC-sourced). |
| `Fred__ApiKey` | [fred.stlouisfed.org](https://fred.stlouisfed.org/docs/api/api_key.html) | The FRED economic-indicator scraper skips gracefully. |
| `MCP_API_KEY` | Self-generated | MCP server is open access (no `Authorization: Bearer` required). |

## Authentication

| Variable | Effect |
|---|---|
| `AUTH_USERNAME` + `AUTH_PASSWORD` | When both are set, the Web portal requires login. When either is empty, the portal is open. |
| `MCP_API_KEY` | When set, MCP requires `Authorization: Bearer <key>`. When empty, MCP is open. |

- Auth is environment-driven on purpose — credentials never live in the repo or in the database.
- `/healthz` on the Web host stays anonymous so Docker Compose health checks still pass when auth is enabled.
- Data-protection keys persist to `/app/keys` (the `web-keys` named volume) so the auth cookie survives container restarts.

## Worker tuning

| Variable | Default | Effect |
|---|---|---|
| `Worker__MinSyncDate` | `2020-01-01` | Earliest date scrapers will fetch from on a fresh install. Set as far back as `2000-01-01` for full history, or to a recent date for a faster first run. Ignored once the DB has data — subsequent runs resume from `max(date) + 1`. |
| `Worker__TickersToSync__N` (array) | unset → all tickers | Restrict every scraper to a list of tickers. Each entry as a separate env var: `Worker__TickersToSync__0=AAPL`, `Worker__TickersToSync__1=MSFT`, etc. |

## Per-scraper tuning

Each scraper has its own option section. All scraper option binds live in [`src/Equibles.Worker.Host/Program.cs`](../../src/Equibles.Worker.Host/Program.cs). The most useful keys:

| Section | Purpose |
|---|---|
| `Finra` | FINRA API credentials (`Finra__ClientId`, `Finra__ClientSecret`). |
| `FinraScraper` | FINRA worker cadence + filtering. |
| `Fred` | FRED API key (`Fred__ApiKey`). |
| `FredScraper` | FRED worker cadence + series-list filtering. |
| `FtdScraper` | SEC Fails-To-Deliver worker cadence. |
| `FinancialFactsScraper` | XBRL fact ingestion cadence. |
| `YahooPriceScraper` | Yahoo price worker cadence + per-ticker backoff. |
| `CftcScraper` | CFTC COT worker cadence + contract subset. |
| `CboeScraper` | CBOE worker cadence. |
| `DocumentScraper` | SEC document download cadence + concurrency. |
| `Worker` | Cross-cutting: `MinSyncDate`, `TickersToSync`. |

Read the matching `<Module>.HostedService/Configuration/<Module>ScraperOptions.cs` for the exact keys each section supports — they vary by source and don't share a common schema.

## Embedding profile (opt-in)

Vector embeddings power semantic search over SEC documents. Disabled by default to keep the base install lean.

```bash
docker compose --profile embedding up
```

Activating the profile adds two services and swaps the default worker:

| Service | Image | Role |
|---|---|---|
| `embedding` | `ollama/ollama` | Ollama runtime on port `11434`. |
| `embedding-pull` | `ollama/ollama` | One-shot init container that runs `ollama pull bge-m3` after `embedding` becomes healthy. |
| `worker-embedding` | Equibles worker image | Replaces the default `worker` with `Embedding__Enabled=true` + the BGE-M3 endpoint. |

Embedding-related variables (override only if running Ollama elsewhere):

| Variable | Default |
|---|---|
| `Embedding__Enabled` | `false` (or `true` under the embedding profile's worker) |
| `Embedding__BaseUrl` | `http://embedding:11434` (the in-network DNS name) |
| `Embedding__ModelName` | `bge-m3` |
| `Embedding__BatchSize` | `10` |

The MCP server's `RagManager` reads the same `EmbeddingConfig` binding for query-time embedding; the worker reads it for chunk-time embedding. Without both bound to a working endpoint, `RagSearchTools` returns empty results.

## Logging

| Variable | Default | Levels |
|---|---|---|
| `MINIMUM_LOG_LEVEL` | `Warning` | `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal` |

Bumping to `Information` is useful for diagnosing slow scraper cycles; `Debug` is firehose-level.

## Update notifications

| Variable | Default | Effect |
|---|---|---|
| `CHECK_FOR_UPDATES` | `true` | The Web portal polls GitHub Releases on a schedule and shows a banner when a newer version is available. Set to `false` to disable the check entirely (recommended for air-gapped deploys). |

## Compose services

| Service | Image | Ports | Role |
|---|---|---|---|
| `db` | `paradedb/paradedb:latest` | `5432:5432` | Postgres + pgvector + pg_search. Required. |
| `web` | built from `src/Equibles.Web/Dockerfile` | `8080:8080` | Web portal. Owns migration application. |
| `mcp` | built from `src/Equibles.Mcp.Server/Dockerfile` | `8081:8080` | MCP transport at `/mcp`. Depends on `web` being healthy. |
| `worker` | built from `src/Equibles.Worker.Host/Dockerfile` | — | Background scrapers. Depends on `web` being healthy. |
| `embedding` (profile `embedding`) | `ollama/ollama:latest` | `11434:11434` | Ollama for embeddings. |
| `embedding-pull` (profile `embedding`) | `ollama/ollama:latest` | — | Pulls the BGE-M3 model once and exits. |
| `worker-embedding` (profile `embedding`) | built from `src/Equibles.Worker.Host/Dockerfile` | — | Worker with `Embedding__Enabled=true`. Replaces the default `worker`. |

## Upgrading

```bash
git pull
docker compose up -d --build
```

- Migrations apply automatically on `web` startup via `MigrateAsync()`. See [Migrations](migrations.md) for details (1-hour command timeout absorbs first-run index rebuilds; BM25 / pgvector indexes can take minutes).
- Read [`CHANGELOG.md`](../../CHANGELOG.md) before a major version bump — breaking changes (rare) get a callout there.
- The Web portal's update banner (when `CHECK_FOR_UPDATES=true`) tells you when a newer release is available.

## Volumes

Two named volumes survive container rebuilds:

| Volume | Mounted at | Holds |
|---|---|---|
| `db-data` | `/var/lib/postgresql` on `db` | Everything — your data. |
| `web-keys` | `/app/keys` on `web` | Data-protection keys for auth cookies + anti-forgery. |
| `ollama-data` (profile `embedding`) | `/root/.ollama` on `embedding` | The downloaded BGE-M3 model (~1.2 GB). |

A `docker compose down` keeps these volumes. `docker compose down -v` deletes them — only use when you want to wipe the database.

## Backups

- Snapshot the `db-data` volume with your usual Postgres tooling: `docker compose exec db pg_dump -U postgres equibles | gzip > equibles-$(date +%F).sql.gz`.
- Restoring is the reverse: `gunzip -c equibles-….sql.gz | docker compose exec -T db psql -U postgres equibles`.
- The other volumes (`web-keys`, `ollama-data`) are regenerable. Losing `web-keys` invalidates outstanding auth cookies (users have to log in again); losing `ollama-data` means re-downloading the embedding model.

## Monitoring

- **Status dashboard** at `~/Status` on the Web host — recent worker errors, per-domain row counts, version banner.
- The dashboard reads from the `Error` table that every scraper writes to via `ErrorReporter` (see [Scrapers → Error reporting](scrapers.md#error-reporting)).
- **Health check** at `~/healthz` on the Web host — anonymous; Compose uses it as `web`'s health probe.
- **Container logs** — `docker compose logs -f <service>`. Logs honor `MINIMUM_LOG_LEVEL`.
- **Postgres metrics** — standard Postgres tooling (`pg_stat_*` views) against the `db` service.

## Air-gapped deploys

- Set `CHECK_FOR_UPDATES=false` to stop the GitHub Releases poll.
- Do not activate the `embedding` profile unless your network can reach the upstream Ollama image and the BGE-M3 model. Pre-pulling and saving via `docker image save` works.
- Most scrapers will simply error if their upstream is unreachable — those errors land on the Status dashboard, not the container's stderr alone.

## Common operational issues

- **Migration takes a long time on first boot** — expected; BM25 + pgvector indexes build during the first `MigrateAsync`. Don't kill the `web` container; the 1-hour command timeout is intentional.
- **MCP returns 401** — `MCP_API_KEY` is set but the client isn't sending `Authorization: Bearer <key>`. To open access, unset `MCP_API_KEY` and `docker compose up -d`.
- **No data in the portal after `docker compose up`** — scrapers take minutes to hours for the initial backfill, longer the further back `Worker__MinSyncDate` goes.
- Watch the Status page for progress; data starts showing up as soon as the first cycle of each scraper writes a row.
- **`CREATE EXTENSION "pg_search" does not exist`** during migration — you're running against a vanilla Postgres image instead of `paradedb/paradedb`. See [Migrations → Postgres extensions](migrations.md#postgres-extensions).
- **FINRA / FRED scrapers always error** — their API key is set incorrectly. Unset both vars to let them skip gracefully (the rest of the platform runs without them).
