# Architecture

Equibles is a modular monolith. Each financial domain ships as a set of layered NuGet projects; three host applications compose those projects into runnable services at startup.

## Composition

- One shared `EquiblesDbContext` ([`src/Equibles.Data/EquiblesDbContext.cs`](../../src/Equibles.Data/EquiblesDbContext.cs)) — a single `DbContext` against ParadeDB (Postgres + `pgvector` + `pg_search`).
- The context owns no model declarations of its own. `OnModelCreating` iterates injected `IModuleConfiguration` instances and lets each one register its entities.
- `IModuleConfiguration.ConfigureEntities(ModelBuilder)` ([`src/Equibles.Data/IModuleConfiguration.cs`](../../src/Equibles.Data/IModuleConfiguration.cs)) is the entire surface a module exposes to the context.
- `EquiblesModuleBuilder` ([`src/Equibles.Data/EquiblesModuleBuilder.cs`](../../src/Equibles.Data/EquiblesModuleBuilder.cs)) is the fluent registration API — `AddModule<T>()` dedupes by type, `AddAllModules()` reflects across already-loaded `Equibles.*` assemblies and picks up every public `IModuleConfiguration` with a parameterless constructor.

## Module shape

Every financial-domain module lives under `src/Equibles.<Module>.*` and uses up to five layered projects. Create only what the module needs.

| Suffix | Role | Pattern |
|---|---|---|
| `.Data` | Entities, EF configuration, `IModuleConfiguration` | Required for any module that owns tables |
| `.Repositories` | Data access, inherits `BaseRepository<T>`; returns `IQueryable<T>` | Required when other layers need to read the module's tables |
| `.BusinessLogic` | Managers — validation + write paths | Optional; add when writes need invariants beyond raw EF |
| `.Mcp` | MCP tool classes (`[McpServerToolType]`) | Optional; add when AI assistants should query the module |
| `.HostedService` | `BackgroundService` scrapers / processors + workers | Optional; add when the module ingests external data |

Dependencies between modules go through the registration extension method. `Equibles.Sec.Data` calls `builder.AddCommonStocks()` and `builder.AddMedia()` from its own `AddSec()` extension — pulling them in transparently so a host that asks for `AddSec()` gets a working SEC module without listing every prerequisite by hand.

## Host composition

Three host applications:

- `src/Equibles.Web` — ASP.NET Core MVC portal (DaisyUI + Tailwind v4 + Vite).
- `src/Equibles.Mcp.Server` — MCP transport server exposing module tools at `/mcp`.
- `src/Equibles.Worker.Host` — background scrapers and processors.

Every host runs the same five-step startup, varying only in which optional layers it wires up:

1. `Equibles.Plugins.PluginLoader.LoadAll()` — loads any optional plugin assemblies first so reflection-based registration sees them.
2. `services.AddEquiblesDbContext(connectionString, modules => modules.AddAllModules().AddMessaging(), migrationsAssembly: …)` — composes the shared DbContext.
3. `services.AddAllRepositories()` — reflects across loaded `Equibles.*` assemblies and registers every `BaseRepository<T>` as scoped.
4. `services.AutoWireServicesFrom<T>()` — assembly-scoped DI registration driven by `[Service]` attributes; called once per assembly the host wants to wire (Equibles.Errors, Equibles.CommonStocks, etc.).
5. Host-specific wiring — controllers + Razor views (Web), MCP tool registration (`mcp.AddHoldings()`, …), or `BackgroundService` workers (`services.AddHoldingsWorker()`, …).

`AddMessaging()` must be called **explicitly** on any host that validates migrations. The MassTransit outbox tables sit in the shared migration snapshot, so omitting them triggers `PendingModelChanges`. `AddAllModules()` only sees assemblies that are already loaded; the explicit call guarantees the messaging module joins the snapshot deterministically.

## Data flow

```
Data (entities) → Repositories (IQueryable) → Managers (write paths) → Controllers / MCP tools / HostedServices
```

- Repositories return `IQueryable<T>`, never materialized collections. Composition happens at the caller.
- Controllers and MCP tools may call repositories directly for **reads**.
- All **writes** route through a manager. Managers own validation, uniqueness checks, and any cross-entity invariants.
- HostedServices write through repositories or managers; they never construct `EquiblesDbContext` directly.

## Search and global discovery

- `services.AddEquiblesSearch()` (called only by the Web host) discovers every `ISearchProvider` across loaded `Equibles.*` assemblies. Each module can ship its own search provider without the host knowing.
- `Equibles.Search.Abstractions` defines the `ISearchProvider` contract. Modules opt in by referencing it and shipping a class that implements the contract.

## EF and database

- Single migrations assembly: [`src/Equibles.Migrations`](../../src/Equibles.Migrations). Every host passes `migrationsAssembly: typeof(Equibles.Migrations.DesignTimeDbContextFactory).Assembly` when calling `AddEquiblesDbContext`.
- Migrations apply on host startup via `await dbContext.Database.MigrateAsync()` with a 1-hour command timeout for index rebuilds.
- The Npgsql provider is configured with `UseVector()` (pgvector), `UseParadeDb()` (BM25 / `pg_search`), `UseQuerySplittingBehavior(SplitQuery)`, and lazy-loading proxies.

## Smart enums

- Domain-specific enums like `DocumentType` and `ErrorSource` use a smart-enum pattern: extensible via a `Register()` method.
- New variants land in the consuming module's startup code, never by editing the core enum type.

## What lives where

| Concern | Project |
|---|---|
| `EquiblesDbContext` + module builder | `Equibles.Data` |
| `BaseRepository<T>` | `Equibles.Data` |
| Migrations snapshot | `Equibles.Migrations` |
| Outbound HTTP clients (SEC, FRED, Yahoo, FINRA, CFTC, CBOE) | `Equibles.Integrations.<Source>` |
| Smart-enum types, shared utilities | `Equibles.Core` |
| MCP host wiring (`AddModule`, middleware) | `Equibles.Mcp` |
| Cross-module search abstractions | `Equibles.Search.Abstractions`, `Equibles.Search` |
| Optional plugin loader | `Equibles.Plugins` |
| MassTransit outbox configuration | `Equibles.Messaging` |
