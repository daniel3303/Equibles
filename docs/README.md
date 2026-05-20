# Equibles Documentation

Reference for developers working inside the Equibles codebase. The root [`README.md`](../README.md) is the user-facing front door (install, run, connect AI assistants); the pages here are for contributors and operators who need to reason about the internals.

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

All pages above are **planned**. None are written yet. New pages land one per PR via `/update-docs`, in the order listed.
