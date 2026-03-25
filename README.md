# Equibles

An open-source, self-hosted mini Bloomberg Terminal for AI agents. Scrapes, stores, and serves SEC filings, institutional holdings, insider trading, congressional trades, and short data — and exposes it all via MCP so your AI assistant can query it directly.

Powers [equibles.com](https://equibles.com).

## What's Included

| Domain | Data Source | Description |
|--------|------------|-------------|
| **SEC Filings** | SEC EDGAR | 10-K, 10-Q, 8-K annual/quarterly/current reports with full-text search |
| **Holdings** | SEC 13F-HR | Institutional ownership — who owns what, how much, and trend over time |
| **Insider Trading** | SEC Form 3/4 | Director, officer, and 10% owner transactions |
| **Congressional Trading** | House/Senate disclosures | Stock trades by members of Congress |
| **Short Data** | FINRA | Daily short volume, short interest, and fails-to-deliver |

## Quick Start

### Docker Compose (recommended)

The fastest way to get everything running. Requires Docker.

```bash
git clone https://github.com/equibles/equibles.git
cd equibles
docker compose up
```

This starts:

| Service | Port | Description |
|---------|------|-------------|
| **db** | 5432 | ParadeDB (PostgreSQL + pgvector + pg_search) |
| **web** | 8080 | Web portal for browsing data |
| **mcp** | 8081 | MCP server for AI assistants |
| **worker** | — | Scrapers (SEC, Holdings, Congress, Short Data) |

Data scraping starts automatically. SEC filings, holdings, insider trades, and congressional trades will begin populating within minutes.

### With Vector Embeddings (opt-in)

Vector embeddings enable semantic search over SEC filings (e.g., "find revenue growth discussion in Apple's 10-K"). This requires downloading the Ollama runtime (~2GB) and the BGE-M3 model (~1.2GB).

```bash
docker compose --profile embedding up
```

This adds:

| Service | Port | Description |
|---------|------|-------------|
| **embedding** | 11434 | Ollama server with BGE-M3 model |
| **worker-embedding** | — | Worker with embedding generation enabled |

Without the embedding profile, BM25 full-text search via ParadeDB still works out of the box — vector search is purely additive.

### From Source

Requires .NET 10 SDK and a PostgreSQL database with pgvector and ParadeDB extensions.

```bash
# Build
dotnet build Equibles.sln

# Run the worker (starts all scrapers)
dotnet run --project src/Equibles.Worker.Host

# Run the MCP server
dotnet run --project src/Equibles.Mcp.Server

# Connection string (appsettings.json or environment variable)
ConnectionStrings__DefaultConnection="Host=localhost;Database=equibles;Username=postgres;Password=postgres"
```

### Configuration

All settings can be configured via a `.env` file in the project root (recommended for Docker) or environment variables:

```env
# .env
ConnectionStrings__DefaultConnection=Host=localhost;Database=equibles;Username=myuser;Password=mypassword
Finra__ClientId=your-client-id
Finra__ClientSecret=your-client-secret
```

**FINRA Short Data (free API key required):**

The short data scraper requires a free FINRA API key. Without it, the scraper skips gracefully and all other scrapers run normally.

To get a key:
1. Create a free account at [developer.finra.org](https://developer.finra.org/)
2. Go to **Teams & Apps** and create a new application
3. Copy the **Client ID** and **Client Secret**
4. Set `Finra__ClientId` and `Finra__ClientSecret` in your `.env` file or environment variables

**Ticker Filtering (optional):**

By default, all tickers are synced. To limit data syncing to specific stocks, add ticker lists to your `.env` file or Docker Compose environment. When not set, all stocks are synced.

```env
# .env — sync only these tickers (applies to all scrapers)
DocumentScraperOptions__TickersToSync__0=AAPL
DocumentScraperOptions__TickersToSync__1=MSFT
DocumentScraperOptions__TickersToSync__2=GOOGL
HoldingsScraperOptions__TickersToSync__0=AAPL
HoldingsScraperOptions__TickersToSync__1=MSFT
HoldingsScraperOptions__TickersToSync__2=GOOGL
CongressScraperOptions__TickersToSync__0=AAPL
CongressScraperOptions__TickersToSync__1=MSFT
CongressScraperOptions__TickersToSync__2=GOOGL
FinraScraperOptions__TickersToSync__0=AAPL
FinraScraperOptions__TickersToSync__1=MSFT
FinraScraperOptions__TickersToSync__2=GOOGL
```

Each scraper's ticker list is independent — you can sync SEC filings for 500 stocks but short data for only 10.

**Embedding (opt-in):**

| Setting | Default | Description |
|---------|---------|-------------|
| `Embedding__Enabled` | `false` | Set to `true` to enable vector embedding generation |
| `Embedding__BaseUrl` | — | Ollama or OpenAI-compatible endpoint (e.g., `http://localhost:11434`) |
| `Embedding__ModelName` | — | Model name (e.g., `bge-m3`) |
| `Embedding__BatchSize` | `10` | Texts per embedding batch |

**Authentication (optional):**

| Setting | Default | Description |
|---------|---------|-------------|
| `AUTH_USERNAME` | — | Web portal username (auth disabled if empty) |
| `AUTH_PASSWORD` | — | Web portal password (auth disabled if empty) |
| `MCP_API_KEY` | — | MCP server API key (auth disabled if empty) |

When set, the web portal requires login and the MCP server requires `Authorization: Bearer <key>` header. When unset, everything is open access (default).

> **How configuration works:** Environment variables and `.env` override values defined in `appsettings.json` and `appsettings.Development.json`. For Docker, `.env` is the recommended approach. When running from source with `dotnet run`, you can also create an `appsettings.Development.json` in the host project (it's gitignored) for local overrides.

## MCP Server

The MCP server exposes financial data tools for AI assistants (Claude, ChatGPT, etc.):

- **Institutional Holdings** — Top holders, ownership history, institution portfolios, institution search
- **Insider Trading** — Insider transactions, ownership summary, insider search
- **SEC Documents** — Full-text search, semantic search, document browsing, keyword search within filings

### Connecting to Claude Desktop

Add this to your Claude Desktop config file (`claude_desktop_config.json`):

**macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
**Windows**: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "equibles": {
      "url": "http://localhost:8081/mcp"
    }
  }
}
```

Restart Claude Desktop and the Equibles tools will be available. You can then ask questions like "Who are the top institutional holders of AAPL?" or "Search Apple's latest 10-K for revenue growth discussion."

### Connecting to Claude Code

Add the MCP server to Claude Code:

```bash
claude mcp add equibles --transport http http://localhost:8081/mcp
```

### Connecting to ChatGPT Desktop

Add this to your ChatGPT Desktop config file:

**macOS**: `~/Library/Application Support/com.openai.chat/mcp.json`
**Windows**: `%APPDATA%\com.openai.chat\mcp.json`

```json
{
  "servers": {
    "equibles": {
      "url": "http://localhost:8081/mcp"
    }
  }
}
```

Restart ChatGPT Desktop and the Equibles tools will be available.

### Connecting to OpenClaw

In OpenClaw, add an MCP server with the URL `http://localhost:8081/mcp` (HTTP transport).

### Other MCP Clients

Any MCP-compatible client can connect to `http://localhost:8081/mcp` (HTTP transport).

## Architecture

Each domain is a self-contained module distributed as NuGet packages. A shared `EquiblesDbContext` is composed from modules at startup — you register only what you need:

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

Every module follows the same structure:

```
Equibles.{Module}.Data            Models + EF configuration
Equibles.{Module}.Repositories    Data access (BaseRepository<T>)
Equibles.{Module}.BusinessLogic   Business logic (when needed)
Equibles.{Module}.Mcp             MCP tools (when applicable)
Equibles.{Module}.HostedService   Background scrapers (when applicable)
```

### Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10 |
| Database | PostgreSQL + pgvector + ParadeDB (BM25 full-text search) |
| ORM | EF Core 10 with lazy loading proxies |
| Search | ParadeDB BM25 (built-in) + pgvector semantic search (opt-in) |
| Embeddings | Ollama with BGE-M3 (opt-in, not required) |
| MCP | Model Context Protocol server for AI tool integration |

### Module Dependency Graph

```
Equibles.Data (foundation)
  Equibles.CommonStocks.Data (foundational — most modules depend on this)
    Equibles.Holdings.Data
    Equibles.InsiderTrading.Data
    Equibles.Congress.Data
    Equibles.ShortData.Data
    Equibles.Sec.Data (also depends on Media.Data)
  Equibles.Media.Data (foundational — file/image storage)
  Equibles.Errors.Data (standalone)
```

Modules declare their dependencies automatically. Calling `modules.AddSec()` auto-registers `AddCommonStocks()` and `AddMedia()` if not already added.

## Extending with Custom Modules

The architecture is designed for extension. To add a new domain module:

1. Create `Equibles.YourModule.Data` with models and `IModuleConfiguration`
2. Create `Equibles.YourModule.Repositories` with `BaseRepository<T>` subclasses
3. Register with `modules.AddYourModule()` in the host's `Program.cs`

Smart enums (`DocumentType`, `ErrorSource`) can be extended by calling their `Register()` method — no need to modify the open-source packages.

## License

[AGPL-3.0](LICENSE)

## Author

Daniel Oliveira

[![Website](https://img.shields.io/badge/Website-FF6B6B?style=for-the-badge&logo=safari&logoColor=white)](https://danielapoliveira.com/)
[![X](https://img.shields.io/badge/X-000000?style=for-the-badge&logo=x&logoColor=white)](https://x.com/daniel_not_nerd)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-0077B5?style=for-the-badge&logo=linkedin&logoColor=white)](https://www.linkedin.com/in/daniel-ap-oliveira/)
