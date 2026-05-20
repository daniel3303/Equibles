# Hosts

Three host applications compose the shared module assembly into runnable services. They share the same startup spine (see [Architecture](architecture.md)) and differ only in what they wire on top.

## Web — [`src/Equibles.Web`](../src/Equibles.Web)

The MVC portal for browsing data in a browser.

- Entry: [`Program.cs`](../src/Equibles.Web/Program.cs) (`public partial class Program`, `Main` → `ConfigureServices` → `Build` → `ApplyMigrationsAsync` → `ConfigurePipeline` → `RunAsync`).
- Container: [`Dockerfile`](../src/Equibles.Web/Dockerfile) — base image `dotnet/aspnet:10.0`, `EXPOSE 8080`. `docker-compose.yml` maps `8080:8080`.
- Razor + DaisyUI v5 + Tailwind v4 + Vite. Razor runtime compilation is on (`AddRazorRuntimeCompilation()`) so view edits don't require a rebuild in dev.
- `AddEquiblesSearch()` is wired only here — global search lives in the portal, not the MCP server or worker.
- `AddDataProtection().PersistKeysToFileSystem(/app/keys)` — the `web-keys` volume in `docker-compose.yml` makes anti-forgery / cookie keys survive container restarts.
- Auth scheme `EnvAuthHandler` (`src/Equibles.Web/Authentication/EnvAuthHandler.cs`) gates the portal when `AUTH_USERNAME` + `AUTH_PASSWORD` are set; absent settings = open access.
- Health endpoint `/healthz` is mapped anonymously so the compose health checks (`web` is a dependency of `mcp`) succeed without auth.
- `ApplyMigrationsAsync` runs `dbContext.Database.MigrateAsync()` with a 1-hour command timeout to absorb index rebuilds on first run.

## MCP Server — [`src/Equibles.Mcp.Server`](../src/Equibles.Mcp.Server)

The MCP transport exposing financial-domain tools to AI assistants.

- Entry: [`Program.cs`](../src/Equibles.Mcp.Server/Program.cs) (`public partial class Program`).
- Container: [`Dockerfile`](../src/Equibles.Mcp.Server/Dockerfile) — `dotnet/aspnet:10.0`, `EXPOSE 8080`. Compose maps `8081:8080` so the public endpoint is `http://localhost:8081/mcp`.
- `AddEquiblesMcp(mcp => …)` registers per-module tool sets: `mcp.AddHoldings()`, `AddInsiderTrading()`, `AddFred()`, `AddSec()`, `AddFinancialFacts()`, `AddCftc()`, `AddCboe()`, `AddCongress()`, `AddShortData()`, `AddStockPrices()`. Every module shipping a `.Mcp` project gets one of these calls.
- `ConfigurePipeline` mounts the MCP transport at `/mcp` via `app.MapMcp("/mcp")` and wraps that path in `ApiKeyMiddleware` (under `UseWhen`, so the rest of the app stays open for health checks).
- `IApiKeyValidator` resolves to `SimpleApiKeyValidator`, which reads `McpApiKey` from configuration. Empty / unset = auth disabled.
- Wires `Equibles.Sec.BusinessLogic.Search.RagManager` via `AutoWireServicesFrom<T>` so MCP SEC tools can run semantic search. Requires the `Embedding` config section to be bound to `EmbeddingConfig`; without that, `EmbeddingConfig.Enabled` is false and semantic search no-ops.
- Does **not** run migrations — the `mcp` compose service depends on `web` being healthy, and `web` owns migration application.

## Worker Host — [`src/Equibles.Worker.Host`](../src/Equibles.Worker.Host)

The background-scraper host. Plain `Host.CreateApplicationBuilder` (not `WebApplication`) — no HTTP surface.

- Entry: [`Program.cs`](../src/Equibles.Worker.Host/Program.cs) (top-level statements, not a partial class).
- Container: [`Dockerfile`](../src/Equibles.Worker.Host/Dockerfile) — `dotnet/aspnet:10.0` (for shared runtime), `ENTRYPOINT ["dotnet", "Equibles.Worker.Host.dll"]`, no ports exposed.
- `AddMessaging(builder.Configuration)` configures MassTransit on the Postgres SQL transport with the EF outbox sitting inside `EquiblesDbContext`. The transport connection comes from `ConnectionStrings__TransportConnection`; `MassTransit__RunMigration=true` makes the worker apply the transport schema on first run.
- One scraper-options bind per source (`WorkerOptions`, `DocumentScraperOptions`, `FinraOptions` + `FinraScraperOptions`, `FredOptions` + `FredScraperOptions`, `FtdScraperOptions`, `FinancialFactsScraperOptions`, `YahooPriceScraperOptions`, `CftcScraperOptions`, `CboeScraperOptions`). Each scraper reads its own section so per-source tuning never leaks across modules.
- `AddSecWorker()`, `AddSecFinancialFactsWorker()`, `AddFinraWorker()`, `AddFredWorker()`, `AddYahooWorker()`, `AddCftcWorker()`, `AddCboeWorker()`, `AddCongressWorker()`, `AddHoldingsWorker()` register the `BackgroundService` workers from each `.HostedService` project. `AddWorkerServices()` wires the cross-cutting worker plumbing (`SyncDateResolver`, etc.).
- Per `Directory.Build.props`, every `.HostedService` project shares the global usings `Microsoft.Extensions.{DependencyInjection,Hosting,Logging,Options}` + `Equibles.Data` + `Equibles.Core`.

## Embedding profile

`docker-compose.yml` defines `worker-embedding` under the `embedding` profile alongside an `embedding` Ollama service. Activate with `docker compose --profile embedding up`. The profile substitutes the default `worker` with `worker-embedding`, which sets `Embedding__Enabled=true`, `Embedding__BaseUrl=http://embedding:11434`, `Embedding__ModelName=bge-m3`. The same `EmbeddingConfig` binding the MCP server reads is what enables SEC chunk embedding generation on this worker.

## Shared startup sequence

Every host runs the same five steps described in [Architecture → Host composition](architecture.md#host-composition):

1. `PluginLoader.LoadAll()`
2. `AddEquiblesDbContext(... modules.AddAllModules().AddMessaging() ...)` (Worker calls `AddMessaging` separately as a service registration, not as a module — the module registration still happens via `AddAllModules()`)
3. `AddAllRepositories()`
4. `AutoWireServicesFrom<T>()` (once per assembly to wire)
5. Host-specific registrations

`AddMessaging()` as a *module* call must appear in the Web and MCP hosts; the Worker host pulls the same `MessagingModuleConfiguration` via `AddAllModules()` reflection because it references `Equibles.Messaging` directly. Either way the outbox tables end up in the EF model that owns the migration snapshot.
