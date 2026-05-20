# MCP Tools

Catalog of the MCP tools exposed by each `*.Mcp` project and the wiring path through [`Equibles.Mcp`](../../src/Equibles.Mcp) into [`Equibles.Mcp.Server`](../../src/Equibles.Mcp.Server). Names match what an AI assistant sees in `tools/list`.

## Wiring path

- [`EquiblesMcpBuilder`](../../src/Equibles.Mcp/EquiblesMcpBuilder.cs) wraps the MCP server builder and tracks registered modules + middleware.
- The MCP host calls [`services.AddEquiblesMcp(mcp => { ... })`](../../src/Equibles.Mcp/Extensions/ServiceCollectionExtensions.cs) once.
- That call invokes `services.AddMcpServer().WithHttpTransport()` and hands a fluent `EquiblesMcpBuilder` to the configuration lambda.
- Each module ships an `AddXxx(EquiblesMcpBuilder)` extension under `Equibles.<Module>.Mcp.Extensions.McpBuilderExtensions` that calls `builder.AddModule<AssemblyMcpModule<MarkerType>>()`.
- [`AssemblyMcpModule<TMarker>`](../../src/Equibles.Mcp/AssemblyMcpModule.cs) calls `builder.WithToolsFromAssembly(typeof(TMarker).Assembly)` — discovers every `[McpServerToolType]` class in the module assembly and registers its `[McpServerTool]`-attributed methods.
- The host pipeline mounts the transport at `/mcp` with `app.MapMcp("/mcp")` and gates it via [`ApiKeyMiddleware`](../../src/Equibles.Mcp/Middleware/ApiKeyMiddleware.cs) under `UseWhen(ctx.Request.Path.StartsWithSegments("/mcp"))`.

## Tool catalog

One section per module. Each tool name is exactly what the MCP client sees; the parenthetical class is the source file under `src/Equibles.<Module>.Mcp/Tools/`.

### `mcp.AddHoldings()` — institutional 13F-HR

`InstitutionalHoldingsTools`:

- `GetTopHolders` — top institutional holders of a given ticker for a `ReportDate`.
- `GetOwnershipHistory` — historical ownership trend (shares, value, holder count) per quarter.
- `GetInstitutionPortfolio` — full portfolio of one institution for a `ReportDate`.
- `SearchInstitutions` — name-search returning matching `InstitutionalHolder` rows.
- `GetTopBuyersSellers` — biggest absolute share additions and reductions for a ticker vs. the prior `ReportDate`; flags new and sold-out positions.
- `GetMarketWide13FActivity` — market-wide leaderboard for a quarter, selected by `bucket`: `top-buys`, `top-sells`, `new-positions`, `sold-out-positions`.
- `GetInstitutionSummary` — portfolio header for one filer at a `ReportDate`: AUM, position count, top-10 / top-25 concentration, QoQ turnover, latest / prior dates.
- `GetInstitutionSectorAllocation` — one filer's portfolio grouped by industry / sector for its latest 13F report; stocks lacking a classification collapse into an "Unclassified" row.
- `GetInstitutionQuarterlyActivity` — one filer's position changes vs. the prior quarter, bucketed into Initiated / Increased / Reduced / Exited; optional `bucket` filter to a single bucket.
- `GetFundOverlap` — 13F portfolio overlap between two filers at their latest common `ReportDate`: Jaccard similarity, dollar-weighted overlap, and a side-by-side stock table with per-fund shares + percent of portfolio.
- `GetConsensusHoldings` — combined portfolio of 2-25 filers at their latest common `ReportDate`; stocks ranked by holder count then combined value, with optional `minFunds` floor.

### `mcp.AddInsiderTrading()` — Form 3 / 4

`InsiderTradingTools`:

- `GetInsiderTransactions` — recent transactions for a ticker, filterable by transaction code.
- `GetInsiderOwnership` — current insider ownership summary for a ticker.
- `SearchInsiders` — search insiders by name / company / role.

### `mcp.AddSec()` — SEC filings

`DocumentTextTools`:

- `SearchDocumentKeyword` — BM25 keyword search inside a specific document.
- `ReadDocumentLines` — read a slice of a normalized SEC document by line range.

`RagSearchTools` (semantic search; requires `EmbeddingConfig.Enabled = true`):

- `SearchDocuments` — vector search across all indexed SEC documents.
- `SearchCompanyDocuments` — vector search scoped to one company.
- `SearchDocument` — vector search within a single document.
- `ListCompanyDocuments` — list documents available for a company.

`FailToDeliverTools`:

- `GetFailsToDeliver` — FTD records for a ticker over a date range.

### `mcp.AddFinancialFacts()` — XBRL facts

`FinancialFactsTools`:

- `GetFinancialFact` — time series for one XBRL concept (e.g. `us-gaap:Revenues`) on one ticker.
- `CompareFinancialFact` — same concept compared across multiple tickers.

`FinancialStatementTools`:

- `GetFinancialStatement` — full income statement / balance sheet / cash flow for a ticker + fiscal period.

### `mcp.AddCongress()` — congressional trading

`CongressTools`:

- `GetCongressionalTrades` — trades for a ticker across all members.
- `GetMemberTrades` — trades by one member of Congress.
- `SearchCongressMembers` — search members by name / chamber / position.

### `mcp.AddFred()` — FRED economic indicators

`FredTools`:

- `GetEconomicIndicator` — observations for a FRED series (e.g. `DGS10`, `UNRATE`).
- `GetLatestEconomicData` — latest snapshot across the curated macro indicators.
- `SearchEconomicIndicators` — keyword search across series titles / categories.

### `mcp.AddStockPrices()` — Yahoo OHLCV + technical indicators

`StockPriceTools`:

- `GetStockPrices` — daily OHLCV + `AdjustedClose` for a ticker over a date range.
- `GetLatestPrices` — latest close for one or more tickers.
- `GetStochasticOscillator` — Stochastic Oscillator (%K and %D) for a ticker over a date range.
- `GetAverageTrueRange` — Wilder's Average True Range (ATR) volatility measure for a ticker over a date range.
- `GetOnBalanceVolume` — On-Balance Volume (OBV) cumulative-flow indicator for a ticker over a date range.

### `mcp.AddShortData()` — FINRA + SEC FTD

`ShortDataTools`:

- `GetShortVolume` — daily short-volume time series (FINRA).
- `GetShortInterest` — bi-monthly short-interest reports for a ticker.
- `GetShortInterestSnapshot` — latest short-interest snapshot across tickers.

### `mcp.AddCftc()` — CFTC Commitments of Traders

`CftcTools`:

- `GetCftcPositioning` — non-commercial / commercial / non-reportable positions over time for a contract.
- `GetLatestCftcData` — latest weekly snapshot across all tracked contracts.
- `SearchCftcMarkets` — search the CFTC contract universe.

### `mcp.AddCboe()` — CBOE indicators

`CboeTools`:

- `GetPutCallRatios` — put/call ratios by category (equity, index, total, VIX, ETP).
- `GetVixHistory` — daily VIX OHLC history (1990-present once backfilled).

## Tool implementation conventions

- Every tool method runs through [`McpToolExecutor.Execute`](../../src/Equibles.Mcp/McpToolExecutor.cs); it wraps the body in a try/catch, logs failures, and reports them via the injected `ErrorManager` so they show on the Status dashboard.
- On failure, `McpToolExecutor.Execute` returns a safe `"An error occurred while executing <ToolName>..."` string instead of throwing across the MCP boundary.
- Tool classes carry `[McpServerToolType]`; tool methods carry `[McpServerTool(Name = "...")]` + a `[Description]` so the MCP client gets a usable schema.
- Tools return Markdown-formatted strings (tables for tabular data, headings for grouped output). The MCP transport ferries strings; no binary payloads.
- Tools call repositories directly for reads. Writes are not exposed through MCP — the surface is intentionally read-only.
- Date parameters accept `YYYY-MM-DD`; `null` / missing defaults to "latest available" (each tool documents its own default in its `[Description]`).

## Auth

- `IApiKeyValidator` ([`Equibles.Mcp.Contracts.IApiKeyValidator`](../../src/Equibles.Mcp/Contracts)) controls whether the API-key check is enforced.
- The MCP host registers [`SimpleApiKeyValidator`](../../src/Equibles.Mcp.Server/SimpleApiKeyValidator.cs), which reads `McpApiKey` from configuration. Empty / unset → `IsEnabled = false` and `ApiKeyMiddleware` lets every request through.
- When enabled, requests must include `Authorization: Bearer <key>`. Malformed or missing headers get a 401 with a JSON body — never an unprocessed pass-through.
- Custom validators can replace `SimpleApiKeyValidator` by registering a different `IApiKeyValidator` implementation in the host's `ConfigureServices`.

## Adding a new MCP tool

- Add the method to the existing `*Tools` class for that module (single tool class per module is the established pattern; only split when the surface grows past ~5 methods).
- Apply `[McpServerTool(Name = "ToolName")]` + `[Description("…")]` on the method, `[Description("…")]` on every parameter.
- Wrap the body in `McpToolExecutor.Execute(...)` so failures are logged + surfaced via `ErrorManager` instead of crashing the MCP request.
- Return Markdown; keep tables compact (≤8 columns) so they render cleanly in chat clients.
- No host change required — `AssemblyMcpModule<TMarker>` discovers the new method via `WithToolsFromAssembly` on the next startup.
