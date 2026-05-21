# Migrations and Database

Every entity in the codebase lives in one shared EF Core migrations history at [`src/Equibles.Migrations`](../../src/Equibles.Migrations). Host applications apply migrations on startup; new migrations are produced via `dotnet ef` against a design-time factory that wires every module by hand.

## Project layout

- [`Equibles.Migrations.csproj`](../../src/Equibles.Migrations/Equibles.Migrations.csproj) — references **every** `Equibles.*.Data` project plus `Equibles.Messaging`.
- The snapshot has to know about every module's entities, so the migrations assembly references them all.
- [`DesignTimeDbContextFactory.cs`](../../src/Equibles.Migrations/DesignTimeDbContextFactory.cs) — `IDesignTimeDbContextFactory<EquiblesDbContext>` that EF tooling invokes for `dotnet ef ...`.
- [`designsettings.json`](../../src/Equibles.Migrations/designsettings.json) — connection string the design-time factory reads.
- Default: `Host=localhost;Port=5432;Database=equibles;Username=equibles;Password=equibles`.
- Override the default locally with `designsettings.Development.json` (gitignored).
- [`Migrations/`](../../src/Equibles.Migrations/Migrations) — generated `<timestamp>_<Name>.cs` files plus the `EquiblesDbContextModelSnapshot.cs`.

## Design-time factory

`DesignTimeDbContextFactory.CreateDbContext`:

- Reads `designsettings.json` (+ optional `.Development.json`) for the connection string.
- Builds `DbContextOptions<EquiblesDbContext>` with `UseNpgsql(connectionString, … UseVector().UseParadeDb().UseQuerySplittingBehavior(SplitQuery).MigrationsAssembly(thisAssembly))` and `UseLazyLoadingProxies()`.
- Instantiates **every** `IModuleConfiguration` by hand into a `IModuleConfiguration[]` and passes it to the `EquiblesDbContext` constructor.

The explicit array is the key difference vs runtime hosts. Hosts use `AddAllModules()` reflection, but reflection only finds assemblies the runtime has already loaded — and `dotnet ef` doesn't load arbitrary `Equibles.*` assemblies during scaffolding. Listing modules by hand guarantees the migrations snapshot sees every entity declared anywhere in the repo. Forgetting to add a new module to this list is the most common cause of "missing table" errors after a fresh migration.

## Adding a migration

From the repo root:

```bash
dotnet ef migrations add <Name> \
  --project src/Equibles.Migrations \
  --startup-project src/Equibles.Migrations
```

- `--startup-project` points at `Equibles.Migrations` itself (not one of the host apps) — the design-time factory has everything EF needs and avoids dragging host-specific DI in.
- Name format: `PascalCase` describing the change, e.g. `AddProcessedFiling`, `WidenInsiderTransactionUniqueIndexForMultiTxPerFiling`.
- A new `Equibles.<Module>.Data` project doesn't appear in migrations until it's added to both `Equibles.Migrations.csproj` (`<ProjectReference>`) and `DesignTimeDbContextFactory.modules`.
- Both edits land in the same PR as the new module.
- Inspect the generated `.cs` before committing.
- `Up`/`Down` are inferred from the snapshot diff and occasionally produce noisier changes than intended (column reorders, index rebuilds) — rewrite if needed.

## Applying migrations

Hosts call `await dbContext.Database.MigrateAsync()` on startup.

- The Web host owns migration application via `ApplyMigrationsAsync` in [`src/Equibles.Web/Program.cs`](../../src/Equibles.Web/Program.cs).
- The method creates a scope, resolves `EquiblesDbContext`, sets `Database.SetCommandTimeout(TimeSpan.FromHours(1))`, and runs `MigrateAsync()`.
- The 1-hour command timeout absorbs index rebuilds on first run; BM25 + pgvector indexes on multi-million-row tables can take many minutes.
- The MCP server does **not** run migrations. Its compose service `depends_on: web (condition: service_healthy)`, so by the time MCP starts the Web host has already finished migrating.
- The Worker host doesn't call `MigrateAsync` on the EF model either, but it does run `MassTransit__RunMigration=true` so the MassTransit SQL transport tables apply on first boot. The EF tables are still owned by Web.
- Migrations apply additively. There's no down-migration path in production — `Down` exists in the file but `MigrateAsync` only applies the unapplied `Up` set.

## Postgres extensions

The initial migration declares two extensions, both required:

- `vector` — pgvector; stores SEC chunk embeddings on `Chunk.Embedding` (`vector` column).
- Disabled embedding (`EmbeddingConfig.Enabled=false`) leaves the column null, but the extension is still required because the column type references it.
- `pg_search` — ParadeDB BM25; powers full-text search over SEC chunk content via a BM25 index.
- Declared via the EF annotation `Npgsql:PostgresExtension:pg_search` plus the index method override (`NpgsqlIndexBuilderExtensions.HasMethod(..., "bm25")`).

Both extensions ship out-of-the-box in the `paradedb/paradedb:latest` Docker image used by `db` in `docker-compose.yml`. On a non-ParadeDB Postgres install, `MigrateAsync` will fail at `CREATE EXTENSION pg_search` with a clear error.

The `Equibles.ParadeDB.EntityFrameworkCore` package provides the `UseParadeDb()` extension that wires the BM25 method into the Npgsql provider's index DSL.

## BM25 indexes

The SEC `Chunk` table carries a BM25 index over `(Id, Content, DocumentType, DocumentId, Ticker, ReportingDate)`:

- Declared in `Equibles.Sec.Data.Models.ChunkConfiguration` via the extension method `HasMethod("bm25")` from the ParadeDB EF provider.
- `RagSearchTools` queries it for vector-free keyword search; `DocumentTextTools.SearchDocumentKeyword` queries it for in-document search.
- First creation is slow (multi-minute on a fully-populated table); subsequent migrations that touch the index incur the same cost.

## `NULLS NOT DISTINCT` unique indexes

Several unique indexes use the Postgres 15+ `NULLS NOT DISTINCT` behaviour so `NULL` values count as duplicates for uniqueness:

```csharp
NpgsqlIndexBuilderExtensions.AreNullsDistinct(
    b.HasIndex("CommonStockId", "InstitutionalHolderId", "ReportDate", "ShareType", "OptionType"),
    false);
```

Without this, `OptionType=NULL` rows wouldn't conflict with each other, and the import pipeline would happily insert duplicate "no option" 13F holdings. The fluent index is declared in the owning module's `IModuleConfiguration.ConfigureEntities` (`HoldingsModuleConfiguration` for the index above), not in the entity attribute (Postgres index syntax doesn't have an `[Index]` analogue for this flag).

## Migration history

The snapshot is the source of truth for the entire EF model. New migrations should never edit existing migration files — instead, add a new `<timestamp>_<Name>` migration that adjusts the schema and re-generates the snapshot.

`Migrations/EquiblesDbContextModelSnapshot.cs` and `<Name>.Designer.cs` are generated. Hand edits get overwritten on the next `dotnet ef migrations add`.

## Common errors

- **`PendingModelChanges` at host startup** — a module's `IModuleConfiguration` is in the runtime model but not in the migration snapshot.
- Most common cause: forgetting `AddMessaging()` in a host that calls `AddAllModules()`; see [Architecture → Host composition](architecture.md#host-composition) for the explicit-call requirement.
- **"relation does not exist" after adding a new module** — the new module's `Equibles.<Module>.Data` was not added to `Equibles.Migrations.csproj` and `DesignTimeDbContextFactory`.
- Fix: make both edits, then run `dotnet ef migrations add`.
- **`CREATE EXTENSION "pg_search" does not exist`** — running against vanilla Postgres instead of ParadeDB. Use the `paradedb/paradedb` Docker image (see `docker-compose.yml`).
- **Migration timeout on first run** — bump `Database.SetCommandTimeout` higher.
- The default 1 hour covers the first-run BM25 index build.
- If you've split a heavy index migration across multiple files, the host applies them sequentially under the same command timeout.
