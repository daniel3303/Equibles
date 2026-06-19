# MCP Tools

Catalog of the MCP tools exposed by each `*.Mcp` project and the wiring path through [`Equibles.Mcp`](../../src/Equibles.Mcp) into [`Equibles.Mcp.Server`](../../src/Equibles.Mcp.Server). Names match what an AI assistant sees in `tools/list`.

## Wiring path

- [`EquiblesMcpBuilder`](../../src/Equibles.Mcp/EquiblesMcpBuilder.cs) wraps the MCP server builder and tracks registered modules + middleware.
- The MCP host calls [`services.AddEquiblesMcp(mcp => { ... })`](../../src/Equibles.Mcp/Extensions/ServiceCollectionExtensions.cs) once.
- That call invokes `services.AddMcpServer().WithHttpTransport()` and hands a fluent `EquiblesMcpBuilder` to the configuration lambda.
- Each module ships an `AddXxx(EquiblesMcpBuilder)` extension under `Equibles.<Module>.Mcp.Extensions.McpBuilderExtensions` that calls `builder.AddModule<AssemblyMcpModule<MarkerType>>()`.
- [`AssemblyMcpModule<TMarker>`](../../src/Equibles.Mcp/AssemblyMcpModule.cs) calls `builder.WithToolsFromAssembly(typeof(TMarker).Assembly)`.
- That call discovers every `[McpServerToolType]` class in the module assembly and registers its `[McpServerTool]`-attributed methods.
- The host pipeline mounts the transport at `/mcp` with `app.MapMcp("/mcp")`.
- [`ApiKeyMiddleware`](../../src/Equibles.Mcp/Middleware/ApiKeyMiddleware.cs) gates that path via `UseWhen(ctx.Request.Path.StartsWithSegments("/mcp"))`.

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
- `GetMostHeldStocks` — cross-sectional ranking of stocks by institutional 13F breadth for a quarter, ordered by filer count (default), quarter-over-quarter change in filer count (warming / cooling), or total reported value; includes Δ filers, total value, Δ value, and the stock's share of the 13F universe.

### `mcp.AddInsiderTrading()` — Form 3 / 4

`InsiderTradingTools`:

- `GetInsiderTransactions` — recent transactions for a ticker, filterable by transaction code.
- `GetInsiderOwnership` — current insider ownership summary for a ticker.
- `SearchInsiders` — search insiders by name / company / role.
- `GetProposedSales` — recent proposed insider sales for a ticker from SEC Form 144 notices: seller, relationship to the company, shares and aggregate market value to be sold, approximate sale date, and broker.

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

`FormDTools`:

- `GetExemptOfferings` — recent exempt offerings (Regulation D private placements) for a company from SEC Form D notices: offering amount, amount sold (dollar figure or `Indefinite`), minimum investment, investor count, claimed exemptions, and amendment flag.

`NportTools`:

- `GetFundHoldings` — portfolio holdings of a fund or ETF from its latest SEC Form NPORT-P monthly report: series, reporting period, net assets, and largest positions (issuer, CUSIP, position size, USD value, share of net assets, asset category). Only registered funds file NPORT-P.
- `GetFundsHoldingStock` — reverse lookup: the registered funds and ETFs holding a given stock, matched by CUSIP against each fund series' most recent NPORT-P report (so an exited position never shows as current). Returns registrant and series, reporting period, position size, USD value, share of the fund's net assets, and payoff profile (Long/Short), largest positions first.

`FundDirectoryTools`:

- `SearchFunds` — search the directory of registered funds and ETFs (NPORT-P filers) by name, ticker, or registrant; returns each series with a profile id, net assets, reported-holding count, and latest report date, largest first. Covers multi-series trusts (iShares, Vanguard, Fidelity) that carry no ticker of their own.
- `GetFundProfile` — one registered fund's profile and largest holdings from its latest NPORT-P report, addressed by a profile id from `SearchFunds` or the fund's own ticker.

`NCenTools`:

- `GetFundOperations` — operational data for a fund, ETF or closed-end fund from SEC Form N-CEN annual reports: registrant classification, Investment Company Act file number, reporting period, first/last-filing flags, and named service providers (advisers, sub-advisers, custodians, transfer agents, administrators, auditors, underwriters). Only registered funds file N-CEN.

`InvestmentAdviserTools`:

- `SearchInvestmentAdvisers` — search SEC-registered investment advisers (Form ADV) by firm name; returns CRD number, main office, regulatory assets under management and employee count, largest by assets first.
- `GetInvestmentAdviser` — full Form ADV profile for one adviser by Organization CRD number: legal and business names, SEC file number, main office, website, regulatory AUM (discretionary, non-discretionary, total), employee count, and fee structure.

### `mcp.AddFinancialFacts()` — XBRL facts

`FinancialFactsTools`:

- `GetFinancialFact` — time series for one XBRL concept (e.g. `us-gaap:Revenues`) on one ticker.
- `CompareFinancialFact` — same concept compared across multiple tickers.

`FinancialStatementTools`:

- `GetFinancialStatement` — full income statement / balance sheet / cash flow for a ticker + fiscal period.

`RevenueBreakdownTools`:

- `GetRevenueBreakdown` — revenue disaggregated by business segment, geography, and product/service from the dimensional XBRL facts the issuer tags in its own filings: annual fiscal years only, latest restated values, one table per axis the company reports; as-reported, never estimated.

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
- `GetBollingerBands` — Bollinger Bands for a ticker over a date range: a middle SMA of close with upper and lower bands set a number of standard deviations above and below it; bands widen as volatility rises.

### `mcp.AddShortData()` — FINRA + SEC FTD

`ShortDataTools`:

- `GetShortVolume` — daily short-volume time series (FINRA).
- `GetShortInterest` — bi-monthly short-interest reports for a ticker.
- `GetShortInterestSnapshot` — latest short-interest snapshot across tickers.
- `GetLargestShortVolume` — stocks with the largest daily short volume for a single trading day (defaults to the latest available), sorted by short volume descending.
- `GetShortSqueezeScores` — stocks ranked by a composite short-squeeze score (0–100, highest first): the equal-weight mean of peer-relative percentiles for short interest as a percent of shares outstanding, days to cover, and the recent change in the short share of total volume, computed across every stock reporting short interest at the latest FINRA settlement date (`ShortSqueezeScoreManager.Compute`).

`OffExchangeVolumeTools`:

- `GetOffExchangeVolume` — weekly off-exchange (dark-pool / OTC) volume per ticker from FINRA OTC/ATS Transparency data: ATS volume and trade count, non-ATS OTC volume and trade count, and the ATS + non-ATS OTC total.

### `mcp.AddCftc()` — CFTC Commitments of Traders

`CftcTools`:

- `GetCftcPositioning` — non-commercial / commercial / non-reportable positions over time for a contract.
- `GetLatestCftcData` — latest weekly snapshot across all tracked contracts.
- `SearchCftcMarkets` — search the CFTC contract universe.

### `mcp.AddCboe()` — CBOE indicators

`CboeTools`:

- `GetPutCallRatios` — put/call ratios by category (equity, index, total, VIX, ETP).
- `GetVixHistory` — daily VIX OHLC history (1990-present once backfilled).

### `mcp.AddGovernmentContracts()` — federal contract awards

`GovernmentContractsTools`:

- `GetGovernmentContracts` — federal contract awards (USAspending.gov) won by one public company, with awarding agency, award amount, period dates, and description.
- `GetTopGovernmentContractors` — rank public companies by total federal contract dollars awarded over a date range.

### `mcp.AddFdaCatalysts()` — FDA advisory-committee meetings

`FdaCatalystTools`:

- `GetFdaCatalysts` — scheduled FDA advisory-committee (AdComm) meetings from the FDA.gov calendar — the regulatory catalyst dates that move biotech and pharma stocks.

## Tool implementation conventions

- Every tool method runs through [`McpToolExecutor.Execute`](../../src/Equibles.Mcp/McpToolExecutor.cs), which wraps the body in a try/catch and logs failures.
- Failures are reported via the injected `ErrorManager` so they show on the Status dashboard.
- On failure, `McpToolExecutor.Execute` returns a safe `"An error occurred while executing <ToolName>..."` string instead of throwing across the MCP boundary.
- Tool classes carry `[McpServerToolType]`; tool methods carry `[McpServerTool(Name = "...")]` + a `[Description]` so the MCP client gets a usable schema.
- Tools return Markdown-formatted strings (tables for tabular data, headings for grouped output). The MCP transport ferries strings; no binary payloads.
- Tools call repositories directly for reads. Writes are not exposed through MCP — the surface is intentionally read-only.
- Date parameters accept `YYYY-MM-DD`; `null` / missing defaults to "latest available" (each tool documents its own default in its `[Description]`).

## Auth

- `IApiKeyValidator` ([`Equibles.Mcp.Contracts.IApiKeyValidator`](../../src/Equibles.Mcp/Contracts)) controls whether the API-key check is enforced.
- The MCP host registers [`SimpleApiKeyValidator`](../../src/Equibles.Mcp.Server/SimpleApiKeyValidator.cs), which reads `McpApiKey` from configuration.
- When `McpApiKey` is empty or unset, `IsEnabled = false` and `ApiKeyMiddleware` lets every request through.
- When enabled, requests must include `Authorization: Bearer <key>`. Malformed or missing headers get a 401 with a JSON body — never an unprocessed pass-through.
- Custom validators can replace `SimpleApiKeyValidator` by registering a different `IApiKeyValidator` implementation in the host's `ConfigureServices`.

## Adding a new MCP tool

- Add the method to the existing `*Tools` class for that module (single tool class per module is the established pattern; only split when the surface grows past ~5 methods).
- Apply `[McpServerTool(Name = "ToolName")]` + `[Description("…")]` on the method, `[Description("…")]` on every parameter.
- Wrap the body in `McpToolExecutor.Execute(...)` so failures are logged + surfaced via `ErrorManager` instead of crashing the MCP request.
- Return Markdown; keep tables compact (≤8 columns) so they render cleanly in chat clients.
- No host change required — `AssemblyMcpModule<TMarker>` discovers the new method via `WithToolsFromAssembly` on the next startup.
