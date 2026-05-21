# Modules

Index of the financial-domain modules in `src/`. Each row is one logical domain; the layered projects per domain follow the suffix shape documented in [Architecture → Module shape](architecture.md#module-shape).

## Index

| Module | Source | Key entities (`*.Data`) | Scraper (`*.HostedService`) | MCP tools (`*.Mcp`) |
|---|---|---|---|---|
| **SEC** ([`Equibles.Sec.*`](../../src/Equibles.Sec.Data)) | SEC EDGAR | `Document` (10-K / 10-Q / 8-K + Form 3/4), `FailToDeliver`, `Chunks/*` | `SecScraperWorker`, `DocumentProcessorWorker`, `FtdScraperWorker` | `Equibles.Sec.Mcp` |
| **SEC Financial Facts** ([`Equibles.Sec.FinancialFacts.*`](../../src/Equibles.Sec.FinancialFacts.Data)) | SEC EDGAR XBRL | `FinancialFact`, `FinancialConcept`, `FinancialFactDimension` | `FinancialFactsScraperWorker` | `Equibles.Sec.FinancialFacts.Mcp` |
| **Holdings** ([`Equibles.Holdings.*`](../../src/Equibles.Holdings.Data)) | SEC 13F-HR | `InstitutionalHolder`, `InstitutionalHolding`, `ProcessedDataSet`, `ProcessedFiling`, `HoldingManagerEntry` | `HoldingsScraperWorker` (bulk), `Holdings13FRealtimeWorker` (per-filing) | `Equibles.Holdings.Mcp` |
| **Insider Trading** ([`Equibles.InsiderTrading.*`](../../src/Equibles.InsiderTrading.Data)) | SEC Form 3 / 4 | `InsiderOwner`, `InsiderTransaction` (with `TransactionCode`, `AcquiredDisposed`, `OwnershipNature` enums) | piggy-backs on SEC — `InsiderTradingFilingProcessor` runs inside `DocumentProcessorWorker` (no scraper project of its own) | `Equibles.InsiderTrading.Mcp` |
| **Congress** ([`Equibles.Congress.*`](../../src/Equibles.Congress.Data)) | House / Senate disclosures | `CongressMember`, `CongressionalTrade` (with `CongressPosition`, `CongressTransactionType` enums) | `CongressionalTradeScraperWorker` | `Equibles.Congress.Mcp` |
| **FRED** ([`Equibles.Fred.*`](../../src/Equibles.Fred.Data)) | Federal Reserve Bank of St. Louis FRED API | `FredSeries`, `FredObservation`, `FredSeriesCategory` | `FredScraperWorker` | `Equibles.Fred.Mcp` |
| **Yahoo Prices** ([`Equibles.Yahoo.*`](../../src/Equibles.Yahoo.Data)) | Yahoo Finance | `DailyStockPrice` (OHLCV + `AdjustedClose`) | `YahooPriceScraperWorker` | `Equibles.Yahoo.Mcp` |
| **FINRA Short Data** ([`Equibles.Finra.*`](../../src/Equibles.Finra.Data)) | FINRA API | `DailyShortVolume`, `ShortInterest` | `FinraScraperWorker` | `Equibles.Finra.Mcp` |
| **CFTC** ([`Equibles.Cftc.*`](../../src/Equibles.Cftc.Data)) | CFTC Commitments of Traders | `CftcContract`, `CftcContractCategory`, `CftcPositionReport` | `CftcScraperWorker` | `Equibles.Cftc.Mcp` |
| **CBOE** ([`Equibles.Cboe.*`](../../src/Equibles.Cboe.Data)) | CBOE | `CboeVixDaily`, `CboePutCallRatio` (with `CboePutCallRatioType` enum) | `CboeScraperWorker` | `Equibles.Cboe.Mcp` |

## Cross-cutting modules

These do not own a financial-domain dataset; they support every other module.

| Module | Role |
|---|---|
| `Equibles.CommonStocks.*` | Stock + ticker + industry/sector taxonomy that every domain references via `CommonStock.Id`. Owned by `CompanySyncService` in `Equibles.Sec.HostedService`. |
| `Equibles.Errors.*` | `Error` entity + `ErrorManager` + `ErrorReporter` — captures scraper/MCP/HostedService failures for the Status dashboard. |
| `Equibles.Media.*` | `File` storage abstraction for raw documents (PDFs, HTML, ZIPs). |
| `Equibles.Search` + `Equibles.Search.Abstractions` | `ISearchProvider` contract + assembly-scoped discovery via `AddEquiblesSearch()`. Each domain module that wants to participate ships a provider class. |
| `Equibles.Messaging` | MassTransit configuration (Postgres SQL transport + EF outbox). |
| `Equibles.Plugins` | Optional plugin assembly loader called as the very first startup step in every host. |

## Module nuances

- **Insider Trading has no `.HostedService` project.** Form 3 / Form 4 filings are SEC documents and arrive through the SEC pipeline.
- [`InsiderTradingFilingProcessor`](../../src/Equibles.Sec.HostedService/Services/InsiderTradingFilingProcessor.cs) runs inside `DocumentProcessorWorker`.
- It parses the form and writes through the Insider Trading repositories.
- **Holdings has two scrapers.** `HoldingsScraperWorker` does the periodic bulk pull; `Holdings13FRealtimeWorker` watches EDGAR for new 13F-HR submissions and ingests them as they post.
- Both scrapers share the same `ProcessedDataSet` / `ProcessedFiling` deduplication bookkeeping.
- **SEC ships three scrapers.**
- `SecScraperWorker` pulls filings.
- `DocumentProcessorWorker` normalises filings and routes them to per-document-type processors.
- `FtdScraperWorker` pulls fails-to-deliver data.
- `Equibles.Sec.FinancialFacts.HostedService` is a separate worker that pulls the XBRL fact stream from the same EDGAR root.
- **SEC Financial Facts ships two XBRL extractors in `Equibles.Sec.FinancialFacts.BusinessLogic/Parsers/`.** `StandaloneXbrlParser` consumes older filings' dedicated `.xml` instance documents; `InlineXbrlParser` consumes the embedded iXBRL of modern `.htm` filings. Both emit the same `ParsedXbrlFact` shape (concept + period + unit + `xbrldi:explicitMember` dimensions) that maps onto `FinancialFact` + `FinancialFactDimension`. **The parsers are not wired into the worker pipeline** — running them at scale requires persisting the raw XBRL artifacts at ingest time (today they live only in transient envelopes; `XbrlStripStep` deletes the inline header before downstream consumers see it). That prerequisite is tracked in [#1118](https://github.com/daniel3303/Equibles/issues/1118); once it lands the import service can call these parsers and start populating dimensional rows.
- **The `*.Mcp` project is optional.**
- A module without AI-assistant-facing tools ships only `.Data` + `.Repositories` (+ `.BusinessLogic` when needed); the current set is `Errors`, `Media`, `CommonStocks` — internal infrastructure.
- The MCP host calls `mcp.AddXxx()` only for modules that expose tools.
- **Module dependencies are declared in the `.Data` extension method.** For example, `Equibles.Sec.Data.Extensions.ModuleBuilderExtensions.AddSec()` calls `AddCommonStocks()` and `AddMedia()` before adding itself.
- Any host that registers SEC gets the prerequisites without listing them.

## Reading a module quickly

Open the module's folder and look at:

1. `<Module>.Data/Models/` — entities; the `[Index]` attributes show the access patterns the schema is tuned for.
2. `<Module>.Data/<Module>ModuleConfiguration.cs` — entity registrations the DbContext picks up.
3. `<Module>.Data/Extensions/ModuleBuilderExtensions.cs` — `AddXxx()` extension; shows the module's dependencies.
4. `<Module>.Repositories/` — the read surface, all `IQueryable<T>`-returning.
5. `<Module>.HostedService/<Module>ScraperWorker.cs` — the cron-style outer loop; delegates the actual import to a `*ImportService` in `Services/`.
6. `<Module>.Mcp/Tools/` — `[McpServerToolType]` classes; the AI-assistant-facing surface.
