# Technical Documentation

Reference for developers working inside the Equibles codebase. Pairs with [`../guide/`](../guide/README.md) (end-user install / usage / how-to / tutorials).

## Topics

- [Architecture](architecture.md) — modular-monolith composition, the shared `EquiblesDbContext` module system, repo → manager → host data flow.
- [Hosts](hosts.md) — the three host apps: `Equibles.Web` (MVC portal), `Equibles.Mcp.Server` (MCP transport), `Equibles.Worker.Host` (background scrapers).
- [Modules](modules.md) — index of the financial-domain modules (SEC, Holdings, Insider, Congress, FRED, Yahoo, FINRA, CFTC, CBOE) with data source, key entities, and scraper entry point per row.
- [MCP tools](mcp-tools.md) — catalog of the tools exposed by each module's `*.Mcp` project and the wiring path through `Equibles.Mcp` → `Equibles.Mcp.Server`.
- [Scrapers and integrations](scrapers.md) — `*.HostedService` worker pattern, `Equibles.Integrations.*` client pattern, rate limiting, retry, `ProcessedDataSet` / `ProcessedFiling` bookkeeping.
- [Web portal](web-portal.md) — `StocksController` tab pattern, `StockTabService`, DaisyUI v5 + Tailwind v4 + Vite, Razor partial conventions.
- [Migrations and database](migrations.md) — `Equibles.Migrations` design-time factory, ParadeDB extensions (`pgvector`, `pg_search`), `MigrateAsync` on host startup.
- [Development](development.md) — Docker-first workflow, `dotnet csharpier` formatting, pre-commit hooks, single-test commands, CI gates (`ci.yml` / `functional.yml` / `smoke.yml`).
- [Operations](operations.md) — environment-variable catalog, `--profile embedding` opt-in, `CHECK_FOR_UPDATES`, auth modes, upgrading.

## Status

All nine pages above are **planned at this path**. Their content lives at `docs/<page>.md` today (the pre-split layout) and will be migrated to `docs/technical/<page>.md` one file per PR via `/update-docs`'s populate lane. The top-level `docs/README.md` index will be refit to point at this section's README once the migration completes.
