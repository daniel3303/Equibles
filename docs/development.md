# Development

How to build, test, format, and contribute to Equibles. The root [`CONTRIBUTING.md`](../CONTRIBUTING.md) is the short version; this page is the developer reference.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (preview channel — `Directory.Build.props` pins `LangVersion=preview`).
- Docker (for the database and the full Compose stack).
- Node.js 20+ (for the Vite frontend bundle under `src/Equibles.Web/`). Only needed if you're rebuilding the frontend locally; the Web Dockerfile builds it for you.
- `pwsh` (only if you intend to run the Playwright functional tests locally — `playwright.ps1 install chromium`).

## Docker-first workflow

Run the full stack through Docker. Fall back to local `dotnet run` only when you genuinely cannot use Docker (debugging a single test, a one-off migration scaffold, the Docker setup is broken).

| Command | What runs |
|---|---|
| `docker compose up db` | Just ParadeDB (Postgres + pgvector + pg_search) on `localhost:5432`. Use this when you're running the .NET hosts locally for debugging. |
| `docker compose up` | Full stack: `db` (5432), `web` (8080), `mcp` (8081), `worker`. |
| `docker compose --profile embedding up` | Adds Ollama on `11434` and a `worker-embedding` variant with embeddings enabled. |
| `docker compose up -d --build` | Rebuild images and run detached after a `git pull`. |

`.env.example` → `.env`; set `SEC_CONTACT_EMAIL` before the first `docker compose up` — SEC EDGAR's fair-access policy requires it.

## Build

```bash
dotnet tool restore         # restores csharpier + dotnet-ef (.config/dotnet-tools.json)
dotnet build Equibles.sln   # full solution; Release config for CI parity:
dotnet build Equibles.sln -c Release
```

- `Directory.Build.props` pins every project to `net10.0`, `LangVersion=preview`, `ImplicitUsings=enable`, `Nullable=disable`. Do not introduce `?` nullability annotations expecting NRT enforcement.
- `*.HostedService` projects inherit global usings (`Microsoft.Extensions.*`, `Equibles.Data`, `Equibles.Core`). No explicit `using` for those in HostedService files.

## Formatting — CSharpier

```bash
dotnet csharpier check .     # CI runs this; fails the PR on unformatted code
dotnet csharpier format .    # fix formatting in place
```

- CSharpier handles all C# formatting. `.editorconfig` covers cross-IDE basics (line endings, indent, charset) but CSharpier is authoritative for `.cs`.
- The local pre-commit hook runs `dotnet csharpier check` on staged C# files only — CI runs it tree-wide, so always run `dotnet csharpier format .` once before pushing if you've touched many files.

## Tests

Five test projects, each with a distinct scope. The `dotnet test` filter at the top level decides which run where:

| Project | Scope | Category filter | Where it runs |
|---|---|---|---|
| [`tests/Equibles.UnitTests`](../tests/Equibles.UnitTests) | Pure unit tests (no DB, no network) | none | every PR (`ci.yml`) |
| [`tests/Equibles.IntegrationTests`](../tests/Equibles.IntegrationTests) | EF Core + Testcontainers (real Postgres in Docker) | none | every PR (`ci.yml`) |
| [`tests/Equibles.FunctionalTests`](../tests/Equibles.FunctionalTests) | Playwright e2e against a real running Web host | `Category=Functional` | every PR (`functional.yml`, separate workflow) |
| [`tests/Equibles.SmokeTests`](../tests/Equibles.SmokeTests) | Live contract tests against real upstream APIs (Yahoo, SEC, FRED, …) | `Category=Live` | nightly only (`smoke.yml`, `cron: 0 6 * * *`) |
| [`tests/Equibles.Benchmarks`](../tests/Equibles.Benchmarks) | BenchmarkDotNet perf harness | not run by `dotnet test` | manual |

### Common test commands

```bash
# Match CI's PR test gate (unit + integration, excluding functional + live):
dotnet test Equibles.sln --filter "Category!=Functional&Category!=Live"

# Just one test project:
dotnet test tests/Equibles.UnitTests/Equibles.UnitTests.csproj

# Single test class or method:
dotnet test tests/Equibles.UnitTests/Equibles.UnitTests.csproj \
  --filter "FullyQualifiedName~InstitutionalHoldingsToolsTests"

# Functional (Playwright) — needs browsers installed once:
pwsh tests/Equibles.FunctionalTests/bin/Debug/net10.0/playwright.ps1 install chromium --with-deps
dotnet test tests/Equibles.FunctionalTests/Equibles.FunctionalTests.csproj

# Live smoke tests (hit real upstream APIs — slow, flaky on network issues):
dotnet test tests/Equibles.SmokeTests/Equibles.SmokeTests.csproj --filter "Category=Live"
```

### Stack

- xUnit + FluentAssertions + NSubstitute (the standard repo stack).
- Naming: `MethodName_Condition_ExpectedResult`; class `{Subject}Tests`; SUT field `_sut`.
- Integration tests use Testcontainers (`paradedb/paradedb:latest`) so they need Docker running.
- Coverage config in [`coverage.runsettings`](../coverage.runsettings) — Cobertura output, excludes `Equibles.Migrations`.

## Pre-commit hooks

Install once (recommended). Hooks run CSharpier + markdownlint + codespell + a handful of hygiene checks on staged files.

```bash
# Pick whichever frontend you prefer:
prek install -f       # Rust port — faster; https://prek.j178.dev
# or
pre-commit install --install-hooks

# One-off run across the whole repo:
prek run --all-files
```

What's wired ([`.pre-commit-config.yaml`](../.pre-commit-config.yaml)):

- `no-commit-to-branch` — refuses commits straight to `main`.
- `mixed-line-ending` → LF, `end-of-file-fixer`, `trailing-whitespace`, `check-merge-conflict`, `check-added-large-files` (`--maxkb=500`), `check-yaml`, `check-json`, `detect-private-key`.
- `markdownlint --fix` against `.markdownlint.yaml`.
- `codespell` against `.codespellrc` + `.codespellignore`.
- Local `csharpier check` on staged `.cs` files.

Hooks aren't authoritative — CI runs the tree-wide gates, so always run `prek run --all-files` once before opening a PR if you've changed many files.

## CI gates

| Workflow | Triggers | What it checks |
|---|---|---|
| [`ci.yml`](../.github/workflows/ci.yml) | every push to `main`, every PR | CSharpier format check → build → `dotnet test --filter "Category!=Functional&Category!=Live"` → Codecov upload → TRX test report |
| [`functional.yml`](../.github/workflows/functional.yml) | every PR | Playwright e2e on the Web host; uploads traces on failure |
| [`smoke.yml`](../.github/workflows/smoke.yml) | nightly cron + manual | `Category=Live` tests against real upstream APIs; never a PR gate |
| [`codeql.yml`](../.github/workflows/codeql.yml) | every PR + scheduled | GitHub CodeQL security scan |
| [`lint-pr-title.yml`](../.github/workflows/lint-pr-title.yml) | every PR | Conventional-Commits PR title check |

`ci.yml` runs lint before test, and the test job depends on lint. A red CSharpier check fails the whole pipeline early.

## Conventions

- **Branches** — `feature/`, `fix/`, `chore/`, `docs/`, `refactor/`, `test/`. Always branch from `main`. Delete the branch on merge (`gh pr merge --squash --delete-branch`).
- **Commits** — Conventional Commits: `feat(holdings): …`, `fix(sec): …`, `docs: …`. Imperative mood, < 72 chars on the subject line.
- **PRs** — single logical change per PR. Title is also Conventional Commits (gated by `lint-pr-title.yml`). PR body: `## Summary` + `## Code changes`.
- **One class per file.** Repositories return `IQueryable<T>`. Controllers are thin. Writes go through managers; reads can call repositories directly.

## Migrations

See [Migrations](migrations.md) for the full workflow. One-liner:

```bash
dotnet ef migrations add <Name> \
  --project src/Equibles.Migrations \
  --startup-project src/Equibles.Migrations
```

The `--startup-project` is `Equibles.Migrations` itself — its `DesignTimeDbContextFactory` knows about every module. Hosts apply migrations on startup via `MigrateAsync`; you don't need to run `dotnet ef database update` locally.

## Adding things

- **New stock tab in the portal** — see [Web portal → Adding a new stock tab](web-portal.md#adding-a-new-stock-tab).
- **New MCP tool** — see [MCP Tools → Adding a new MCP tool](mcp-tools.md#adding-a-new-mcp-tool).
- **New scraper** — see [Scrapers → Adding a new scraper](scrapers.md#adding-a-new-scraper).
- **New financial-domain module** — follow [Module shape](architecture.md#module-shape); also add a project reference + `IModuleConfiguration` row to [`DesignTimeDbContextFactory`](../src/Equibles.Migrations/DesignTimeDbContextFactory.cs) so it lands in migrations.

## Debugging hosts locally

When you need to step through a host, run it locally against the Dockerised database:

```bash
docker compose up db -d                              # just Postgres in a container
# Set ConnectionStrings__DefaultConnection in appsettings.Development.json
# of the host you're running:
dotnet run --project src/Equibles.Web                # localhost:5000
dotnet run --project src/Equibles.Mcp.Server
dotnet run --project src/Equibles.Worker.Host
```

Razor runtime compilation is on in development (`AddRazorRuntimeCompilation`), so `.cshtml` edits live without rebuild. C# changes still require a rebuild.

## Plugin extension

`Equibles.Plugins.PluginLoader.LoadAll()` is the very first startup step in every host. It looks for plugin assemblies under a configurable directory and `Assembly.Load`s them before any reflection-based registration runs (`AddAllModules()`, `AddAllRepositories()`, `AutoWireServicesFrom<T>()`). This is the seam for downstream consumers that want to ship private modules without forking the repo.
