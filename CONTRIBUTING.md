# Contributing to Equibles

Thanks for your interest in contributing! This guide covers the project architecture, development setup, and how to extend the platform with new modules.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL with [pgvector](https://github.com/pgvector/pgvector) and [ParadeDB](https://github.com/paradedb/paradedb) extensions — or just run the ParadeDB Docker image which includes both:
  ```bash
  docker compose up db
  ```

## Development Setup

### 1. Start the Database

The easiest approach is to start only the database from Docker Compose:

```bash
docker compose up db
```

This starts ParadeDB (PostgreSQL + pgvector + pg_search) on port 5432 with default credentials (`postgres`/`postgres`).

### 2. Configure Local Settings

Each host project (`Equibles.Web`, `Equibles.Mcp.Server`, `Equibles.Worker.Host`) reads configuration from the standard ASP.NET Core hierarchy:

```
appsettings.json              → Base defaults (checked into git)
appsettings.Development.json  → Local dev overrides (gitignored)
Environment variables / .env  → Override everything above
```

For local development, create an `appsettings.Development.json` in whichever host project you're running. Example for `src/Equibles.Web/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=equibles;Username=postgres;Password=postgres"
  },
  "Finra": {
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret"
  },
  "DocumentScraperOptions": {
    "TickersToSync": ["AAPL", "MSFT"]
  }
}
```

This file is gitignored, so your local settings won't leak into commits.

> **When to use `.env` vs `appsettings.Development.json`:** Use `.env` for Docker deployments. Use `appsettings.Development.json` when running from source with `dotnet run` — it's more natural for .NET projects and supports nested JSON objects instead of flat `__`-delimited keys.

### 3. Run the Applications

```bash
# Build the entire solution
dotnet build Equibles.sln

# Run the web portal (port 5000 by default in development)
dotnet run --project src/Equibles.Web

# Run the MCP server
dotnet run --project src/Equibles.Mcp.Server

# Run the background worker (starts all scrapers)
dotnet run --project src/Equibles.Worker.Host
```

EF Core migrations run automatically on startup — no manual `dotnet ef database update` needed.

## Project Architecture

### Overview

Equibles is a modular monolith. Each financial domain (Holdings, Insider Trading, SEC, etc.) is a self-contained module distributed as NuGet packages. Three thin host applications compose modules at startup:

| Host | Purpose |
|------|---------|
| `Equibles.Web` | ASP.NET Core MVC web portal (DaisyUI + Tailwind) |
| `Equibles.Mcp.Server` | MCP server exposing tools for AI assistants |
| `Equibles.Worker.Host` | Background scrapers and processors |

### Module Structure

Every domain module follows the same layered pattern. Not all layers are required — create only what the module needs:

```
Equibles.{Module}.Data            → Entity models, EF configuration, IModuleConfiguration
Equibles.{Module}.Repositories    → Data access via BaseRepository<T>
Equibles.{Module}.BusinessLogic   → Managers with business logic (when needed)
Equibles.{Module}.Mcp             → MCP tool definitions (when applicable)
Equibles.{Module}.HostedService   → Background scrapers/processors (when applicable)
```

### All Projects

```
src/
├── Equibles.Data                          Foundation: DbContext, BaseRepository<T>, IModuleConfiguration
├── Equibles.Core                          Shared utilities: AutoWiring, Service attribute
├── Equibles.Integrations.Common           Shared HTTP client utilities
│
├── Equibles.CommonStocks.Data             Stock tickers, exchanges, CUSIPs
├── Equibles.CommonStocks.Repositories
├── Equibles.CommonStocks.BusinessLogic
│
├── Equibles.Holdings.Data                 Institutional ownership (13F filings)
├── Equibles.Holdings.Repositories
├── Equibles.Holdings.Mcp
├── Equibles.Holdings.HostedService
│
├── Equibles.InsiderTrading.Data           Insider transactions (Form 3/4)
├── Equibles.InsiderTrading.Repositories
├── Equibles.InsiderTrading.Mcp
│
├── Equibles.Congress.Data                 Congressional trade disclosures
├── Equibles.Congress.Repositories
├── Equibles.Congress.HostedService
│
├── Equibles.ShortData.Data               Short volume, interest, FTDs
├── Equibles.ShortData.Repositories
├── Equibles.ShortData.HostedService
│
├── Equibles.Sec.Data                      SEC filings and document chunks
├── Equibles.Sec.Repositories
├── Equibles.Sec.BusinessLogic
├── Equibles.Sec.Mcp
├── Equibles.Sec.HostedService
│
├── Equibles.Media.Data                    File/image storage
├── Equibles.Media.Repositories
├── Equibles.Media.BusinessLogic
│
├── Equibles.Errors.Data                   Error tracking
├── Equibles.Errors.Repositories
├── Equibles.Errors.BusinessLogic
│
├── Equibles.Integrations.Sec              SEC EDGAR HTTP client
├── Equibles.Integrations.Finra            FINRA API HTTP client
│
├── Equibles.Mcp                           MCP abstractions (IEquiblesMcpModule)
├── Equibles.Mcp.Server                    ← Host: MCP server
├── Equibles.Web                           ← Host: Web portal
├── Equibles.Worker.Host                   ← Host: Background workers
└── Equibles.Migrations                    EF Core migrations assembly
```

### Module Dependency Graph

```
Equibles.Data (foundation — DbContext, BaseRepository<T>)
│
├── Equibles.CommonStocks.Data (foundational — most modules depend on this)
│   ├── Equibles.Holdings.Data
│   ├── Equibles.InsiderTrading.Data
│   ├── Equibles.Congress.Data
│   ├── Equibles.ShortData.Data
│   └── Equibles.Sec.Data (also depends on Media.Data)
│
├── Equibles.Media.Data (foundational — file/image storage)
└── Equibles.Errors.Data (standalone)
```

Modules declare their dependencies automatically. Calling `modules.AddSec()` auto-registers `AddCommonStocks()` and `AddMedia()` if not already added.

### DbContext Module System

There is one shared `EquiblesDbContext` (plain `DbContext`, no Identity) composed from modules at startup. Each module implements `IModuleConfiguration` to register its entity configurations:

```csharp
builder.Services.AddEquiblesDbContext(connectionString, modules => {
    modules.AddCommonStocks();
    modules.AddHoldings();
    modules.AddInsiderTrading();
    modules.AddCongress();
    modules.AddShortData();
    modules.AddSec();
    modules.AddMedia();
    modules.AddErrors();
});
```

Host applications register only the modules they need. The Worker might register all modules, while a hypothetical microservice might only register `AddHoldings()`.

### Data Flow

```
Data (models) → Repositories (data access) → Managers (business logic) → Controllers / MCP Tools / HostedServices
```

- **Read operations** can call repositories directly from controllers or MCP tools.
- **Write operations** must go through managers for validation and business logic.
- Repositories return `IQueryable<T>` — callers materialize with `.ToListAsync()`.

### MCP Module System

`Equibles.Mcp` provides the `IEquiblesMcpModule` abstraction. Each domain's `.Mcp` package registers its tools. `Equibles.Mcp.Server` is a thin host that composes all MCP modules.

### Worker Module System

Each domain's `.HostedService` package provides a `BackgroundService`. `Equibles.Worker.Host` composes all hosted services — they run in parallel on separate threads.

## Adding a New Module

### 1. Create the Data Project

```bash
dotnet new classlib -n Equibles.YourModule.Data -o src/Equibles.YourModule.Data
```

Add your entity models and implement `IModuleConfiguration`:

```csharp
public class YourModuleConfiguration : IModuleConfiguration {
    public void Configure(ModelBuilder builder) {
        builder.ApplyConfiguration(new YourEntityConfiguration());
    }
}
```

Create the extension method:

```csharp
public static class ModuleBuilderExtensions {
    public static ModuleBuilder AddYourModule(this ModuleBuilder builder) {
        builder.AddCommonStocks(); // if your module depends on stocks
        builder.Add<YourModuleConfiguration>();
        return builder;
    }
}
```

### 2. Create the Repositories Project

```bash
dotnet new classlib -n Equibles.YourModule.Repositories -o src/Equibles.YourModule.Repositories
```

Repositories inherit from `BaseRepository<T>` and handle data access only — no business logic.

### 3. Register in a Host

Add `modules.AddYourModule()` in the host's `Program.cs` and create a migration:

```bash
dotnet ef migrations add AddYourModule --project src/Equibles.Migrations
```

### 4. Optional: Add MCP Tools, Business Logic, or HostedService

Create additional projects only when needed:
- `.BusinessLogic` — for managers with validation or complex write operations
- `.Mcp` — to expose data via MCP tools for AI assistants
- `.HostedService` — for background scrapers or processors

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10 |
| Database | PostgreSQL + pgvector + ParadeDB (BM25 full-text search) |
| ORM | EF Core 10 with lazy loading proxies |
| Search | ParadeDB BM25 (built-in) + pgvector semantic search (opt-in) |
| Embeddings | Ollama with BGE-M3 (opt-in) |
| Frontend | ASP.NET Core MVC + DaisyUI v5 + Tailwind CSS v4 |
| MCP | Model Context Protocol server for AI tool integration |

## Smart Enums

Domain-specific enums like `DocumentType` and `ErrorSource` use a smart enum pattern. They can be extended by calling their `Register()` method — no need to modify the core packages. This is useful when the commercial version adds new document types or error sources.

## Code Style

- Follow existing patterns — consistency over personal preference.
- One class per file.
- Repositories return `IQueryable<T>`, not materialized lists.
- No business logic in repositories — that goes in managers.
- Controllers are thin — HTTP concerns only.
