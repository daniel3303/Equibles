# Contributing to Equibles

Thanks for your interest in contributing! This guide covers how to get started, submit changes, and extend the platform.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (for the database)

### Development Setup

1. **Fork and clone** the repository.

2. **Start the database:**

   ```bash
   docker compose up db
   ```

   This starts ParadeDB (PostgreSQL + pgvector + pg_search) on port 5432 with default credentials (`postgres`/`postgres`).

3. **Configure local settings.** Create `appsettings.Development.json` in the host project you're running (e.g., `src/Equibles.Web/`):

   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Host=localhost;Database=equibles;Username=postgres;Password=postgres"
     }
   }
   ```

   This file is gitignored. See [README.md](README.md#configuration) for optional settings (FINRA, FRED API keys, ticker filtering).

4. **Build and run:**

   ```bash
   dotnet build Equibles.sln

   dotnet run --project src/Equibles.Web            # Web portal (port 5000)
   dotnet run --project src/Equibles.Mcp.Server     # MCP server
   dotnet run --project src/Equibles.Worker.Host    # Background scrapers
   ```

   EF Core migrations run automatically on startup.

## Submitting Changes

1. Create a feature branch from `main`:
   ```bash
   git checkout -b feat/your-feature
   ```
2. Make your changes, following the [code style](#code-style) guidelines below.
3. Ensure the solution builds cleanly: `dotnet build Equibles.sln`
4. Commit with a descriptive message using [Conventional Commits](https://www.conventionalcommits.org/):
   ```
   feat(holdings): add quarterly comparison endpoint
   fix(sec): handle missing CIK in EDGAR response
   docs: update MCP connection instructions
   ```
5. Push your branch and open a pull request against `main`.
6. Keep PRs focused — one logical change per PR.

## Code Style

- **Follow existing patterns** — consistency over personal preference.
- One class per file.
- Repositories return `IQueryable<T>`, not materialized lists.
- No business logic in repositories — that goes in managers.
- Controllers are thin — HTTP concerns only.
- Write operations go through managers for validation and business logic; read operations can call repositories directly.

## Architecture Overview

Equibles is a modular monolith. Each financial domain (Holdings, Insider Trading, SEC, etc.) is a self-contained module distributed as NuGet packages. Three thin host applications compose modules at startup:

| Host | Purpose |
|------|---------|
| `Equibles.Web` | ASP.NET Core MVC web portal (DaisyUI + Tailwind) |
| `Equibles.Mcp.Server` | MCP server exposing tools for AI assistants |
| `Equibles.Worker.Host` | Background scrapers and processors |

### Module Structure

Every domain module follows a layered pattern. Not all layers are required — create only what the module needs:

```
Equibles.{Module}.Data            → Entity models, EF configuration, IModuleConfiguration
Equibles.{Module}.Repositories    → Data access via BaseRepository<T>
Equibles.{Module}.BusinessLogic   → Managers with business logic (when needed)
Equibles.{Module}.Mcp             → MCP tool definitions (when applicable)
Equibles.{Module}.HostedService   → Background scrapers/processors (when applicable)
```

### DbContext Module System

One shared `EquiblesDbContext` composed from modules at startup. Each module implements `IModuleConfiguration` to register its entity configurations:

```csharp
builder.Services.AddEquiblesDbContext(connectionString, modules => {
    modules.AddCommonStocks();
    modules.AddHoldings();
    modules.AddInsiderTrading();
    // ...
});
```

Modules declare their dependencies automatically — calling `modules.AddSec()` auto-registers `AddCommonStocks()` and `AddMedia()` if not already added.

### Data Flow

```
Data (models) → Repositories (data access) → Managers (business logic) → Controllers / MCP Tools / HostedServices
```

## Adding a New Module

### 1. Create the Data Project

```bash
dotnet new classlib -n Equibles.YourModule.Data -o src/Equibles.YourModule.Data
```

Add entity models and implement `IModuleConfiguration`:

```csharp
public class YourModuleConfiguration : IModuleConfiguration
{
    public void Configure(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new YourEntityConfiguration());
    }
}
```

Create the registration extension method:

```csharp
public static class ModuleBuilderExtensions
{
    public static ModuleBuilder AddYourModule(this ModuleBuilder builder)
    {
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

Repositories inherit from `BaseRepository<T>` — data access only, no business logic.

### 3. Register in a Host

Add `modules.AddYourModule()` in the host's `Program.cs` and create a migration:

```bash
dotnet ef migrations add AddYourModule --project src/Equibles.Migrations
```

### 4. Optional Layers

Create additional projects only when needed:

- **`.BusinessLogic`** — Managers with validation or complex write operations
- **`.Mcp`** — MCP tools for AI assistant access
- **`.HostedService`** — Background scrapers or processors

## Smart Enums

Domain-specific enums like `DocumentType` and `ErrorSource` use a smart enum pattern. They can be extended by calling their `Register()` method — no need to modify the core packages.

## License

By contributing, you agree that your contributions will be licensed under the [AGPL-3.0](LICENSE) license.
