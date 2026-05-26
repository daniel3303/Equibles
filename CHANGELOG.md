# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Confidential treatment flag — 13F cover pages' `confidentialTreatmentRequestedFlag`
  is now parsed and stored on `InstitutionalHolder`. The institution profile page
  shows a warning banner when the flag is set, and `GetInstitutionSummary` MCP tool
  appends a note. Helps users understand that a fund's 13F may be incomplete.
- Fund classification — rules-based classifier labels each 13F filer (Bank, Insurance,
  Hedge Fund, Pension, etc.) from the filing manager name. Classification badge shown
  on institution profiles and filterable in the institutions index.
- 13F conviction heat map visualization at `/Holdings/HeatMap`.
- 13F aggregate stats dashboard at `/Holdings/Stats`.
- 13F trend charts (AUM, filer count, sector allocation) at `/Holdings/Trends`.
- Double-down report at `/Holdings/DoubleDown` with threshold filter.
- Institution overlap matrix at `/Institutions/Overlap`.
- Latest 13F filings page at `/Holdings/LatestFilings` with new-filer and amendment badges.
- Insider trading dashboard at `/InsiderActivity/Dashboard` with market-wide
  recent transactions.
- Daily filing activity badge on stock detail page.
- Position-type filter toggles on stock Holdings tab.
- Enriched holders table with ownership %, change %, and quarter first owned.
- Enriched holders CSV export with ownership %, change %, and position type.
- Stock detail page inline key metrics (market cap, P/E, EPS, etc.).
- Compact number toggle for large values in holdings tables.
- Company website URL scraped from SEC EDGAR submissions and persisted on
  `CommonStock`.
- "Current + Combined" date selector in the Holdings tab, showing a merged
  view of the latest quarterly data set and any realtime filings since.
- 13F filing type (`COVER PAGE`, `HOLDINGS`, `AMENDMENT`) parsed and stored
  on `InstitutionalHolding`.
- Many new end-user guide pages (`docs/guide/`): tutorials, how-tos, and FAQ entries.

### Changed

- Institution name matching prefers the shortest match to avoid subsidiary
  collisions (e.g. "BlackRock" now resolves to "BlackRock, Inc." instead of
  "BlackRock Advisors LLC").
- Insider search tokenizes queries so "First Last" order matches "Last First"
  names in the database.
- `GetShortInterestSnapshot` excludes stocks with zero average daily volume
  (previously dominated results with capped days-to-cover of 1000).
- 13F holdings import runs incrementally instead of batching.
- `<cn>` tag helper renamed to `<compactable-number>` for clarity.
- Company name normalization handles common abbreviations (Inc → Inc.,
  Corp → Corp., Ltd → Ltd., etc.), higher roman numerals (IV–X), and
  parenthesized abbreviations.
- Duplicated MCP date-parsing logic extracted into `McpToolExecutor.ParseDateOr`.
- Repeated "stock not found" MCP responses extracted into
  `McpToolExecutor.StockNotFound`.
- SEC MCP `DocumentTextTools` migrated to `McpToolRunner`, matching the
  Execute / ReportError pattern used by the other MCP tool groups.
- LIKE metacharacter escaping extracted into shared `LikePattern` helper.
- Repeated empty-table-row markup extracted into a shared
  `EmptyTableRowTagHelper`.
- Screener CSV export migrated to the shared `CsvExportService` instead of
  hand-rolling its own writer.
- Vite bundles wrapped in IIFE to prevent global scope collision; `bundle.js`
  loaded as ES module.
- Chart.js split into a separate bundle loaded only on chart pages.
- Response compression (Brotli + Gzip) enabled.
- Cache-Control headers added for static assets.
- Unused Inter font weight 300 dropped.

### Fixed

- Cold-start race on a fresh DB volume — the compose healthcheck now forces a
  TCP probe instead of a Unix-socket probe, so it only flips healthy after
  ParadeDB's init phase finishes and the real TCP listener is up. The web
  host also retries `Database.MigrateAsync` on transient connection failures.
- `FiscalPeriodResolver.Resolve` guarded against year-underflow on `AddYears(-1)`.
- `FiscalPeriodResolver.CreateSafe` guarded against year overflow past 9999.
- `FiscalCalendar.GetPeriod` guarded against fiscal year overflow past 9999.
- `FiscalCalendar.GetQuarterEndDate` guarded against calendar year underflow.
- `SyncDateResolver.Resolve` clamped to `DateOnly.MaxValue` on overflow.
- `ParseDataSetEndDate` validates year range before `DaysInMonth` call.
- `TryParseDatePart` validates day-of-month before `DateOnly` construction.
- `HoldingsBacktestCalculator` clamps backtest horizon to `DateOnly.MaxValue`.
- `HoldingsBacktestCalculator` rebalance date overflow past `DateOnly.MaxValue` clamped.
- `Truncate` guards against `IndexOutOfRangeException` when `maxLength` is 0 or negative.
- `Truncate` no longer splits surrogate pairs.
- `ErrorManager.Truncate` uses surrogate-pair-safe boundary handling.
- Holdings position grouper classifies 0-shares-both-quarters as Unchanged, not New.
- Bank fund classifier matches `BANK` as the last word (not just mid-string).
- `InsiderTradingTools.GetRole` handles empty and whitespace-only `OfficerTitle`.
- `FinancialConceptAliases.Normalize` collapses spaced ampersands (`&amp;` → `&`).
- VIX put/call CSV column mapping corrected.
- FINRA API date formatting uses `InvariantCulture`.
- SEC `GetDailyIndex` URL date formatting uses `InvariantCulture`.
- `Realtime13FArchiveBuilder` date formatting uses `InvariantCulture`.
- `HoldingsDataSetClient.FormatDatePart` uses `InvariantCulture` for year.
- CIK leading zeros normalized in the 13F TSV import path.
- Realtime 13F lookback computed dynamically from last quarterly data set.
- `ProcessedDataSetRepository` registered in `DoWork` integration test.
- Holdings integration test CIKs aligned with `TrimStart('0')` normalization.
- Congress `Truncate` handles negative `maxLength`.
- Empty-state message added when no economic indicators are imported.
- 13F import respects `NEW HOLDINGS` amendment type (was silently treated
  as a regular filing).
- `FiscalYearEndMonth` inferred from 10-K filing date when SEC EDGAR
  metadata is null.
- `FiscalYearEndMonth` inferred from 20-F / 40-F for foreign filers
  (previously only 10-K was checked).
- Fiscal year-end day validated against its month (e.g., day 31 rejected
  for months with fewer days).
- Filer-universe query narrowed to only gap holders.
- `ParseTransactionCode` trims input so whitespace-padded SEC transaction
  codes resolve to the correct `TransactionCode` enum value.
- `ParseBool` trims input so whitespace-padded SEC boolean strings
  ("true ", " false") are interpreted correctly.
- `SafeRound` guards against the `decimal.MaxValue` boundary instead of
  throwing on rounding overflow.
- House PTR PDF parser joins multi-line transaction entries so the asset
  name, ticker/dates, and amount land on a single transaction instead of
  three partial rows.
- 13F-HR import aggregates same-key rows across the whole filing instead
  of flushing every 1000 unique keys. When a filer split a position
  across `otherManager` codes the matching rows could fall in different
  batches; the upsert's `WhenMatched` clause REPLACED the persisted row,
  so only the last batch's slice survived (Vanguard's Q4 2025 AAPL came
  out as 39M shares instead of 1.43B). The import now flushes at the
  accession boundary, which SEC guarantees is contiguous in both the
  bulk INFOTABLE and the realtime archive.
- `StocksController.ParsePositionTypes` gates parsed values with
  `Enum.IsDefined` so numeric query input with no matching
  `PositionChangeType` member (e.g. `?types=999`) is rejected the same
  as an unrecognised name, preventing a polluted filter set from
  round-tripping into rendered toggle URLs on the holdings tab.
- `CompanySyncService.NormalizeCompanyName` no longer treats the short English
  words MIX, DIV, LIV, and CIV as Roman numerals — they decompose as 1009,
  504, 54, and 104 respectively but aren't numerals in a company-name context.
  An explicit deny-list rejects exactly those four tokens, so other short
  numerals that use L/C/D/M (XL=40, XC=90, CD=400, CM=900, XLI=41, XLV=45,
  MII=1002) keep working alongside the pure-I/V/X cases.

## [1.1.1] — 2026-05-22

### Added

- Covering index on `InstitutionalHolding (CommonStockId, ReportDate)` with
  `INCLUDE (InstitutionalHolderId, Value, Shares)`. Lets the per-stock
  ownership-trend rollup on `/Stocks/{ticker}/Holdings` run as an index-only
  scan instead of a bitmap heap scan with lossy blocks. Heavy names like
  AAPL (~76k holdings across 18+ quarters) dropped from ~14 s to ~3 s cold
  load locally; warm cached responses are unaffected. The annotation is
  inlined directly into the `Initial` migration — fresh deployments and
  upgrades from earlier `1.1.x` both run a single `CreateIndex` with the
  `INCLUDE` list attached.
- New end-user guide pages (`docs/guide/`): how-to — change how far back
  data syncs; how-to — use the existing embedding endpoint; FAQ —
  disable the update-available banner; FAQ — how much disk space
  Equibles needs; FAQ — wipe the database and start over.

### Changed

- `IDocumentPersistenceService.Save` accepts `accessionNumber` as an optional
  parameter (`= null`) so non-SEC document callers (e.g. earnings-call
  transcripts) don't have to thread a value they don't have. SEC scrapers
  still pass the accession number explicitly; the FinancialFacts importer
  continues to link facts to documents via the existing
  `WHERE AccessionNumber IS NOT NULL` filter.

### Fixed

#### SEC document normalization

- `TableNormalizationStep` no longer drops rows whose only content is an
  `<img>` (or any other non-text visual element — `<br>`, `<canvas>`,
  `<svg>`, `<iframe>`). The previous behavior erased signature-image rows
  on 10-K signature pages because `IsOnlyWhitespaceSpan` treated "zero
  spans" as "visually empty".
- `TableNormalizationStep` also no longer drops *columns* whose cells each
  contain only a non-text visual element. `IsColumnEmpty` previously
  checked `cell.TextContent` alone; the row-side HTML-aware emptiness
  check is now shared between rows and columns.
- `CurrencyConsolidationStep` no longer treats unrelated uppercase
  acronyms (`USDA`, `USDC`, `EUREKA`) as currency cells. ISO code
  detection switched from `text.Contains(code)` to a word-boundary match,
  so a row label like "USDA inspected facilities" is left intact and no
  longer triggers a spurious "All values are in US Dollars." note.
- `CurrencyConsolidationStep` re-applies the per-row gate during the
  processing pass, so a header row with a non-empty next cell (e.g. a
  "Q1" label in column 1) is no longer silently overwritten when another
  row in the same column trips the consolidation gate.
- `PaginationRemovalStep` requires a word boundary after `Part` when
  scanning paragraphs after an `<hr>`. Previously a paragraph starting
  with "Partnership agreement", "Particular circumstances", or "Parts
  inventory" was removed alongside genuine "Part I" / "Part II" headers.
- `HeadingConversionStep.IsPartHeading` no longer throws
  `IndexOutOfRangeException` when the post-`PART` suffix is composed
  entirely of split delimiters (e.g. `Part -`). The throw used to abort
  the whole normalization pipeline for the affected filing.

#### SEC financial facts (XBRL)

- `StandaloneXbrlParser.ResolveUnit` trims leading/trailing whitespace
  around the `xbrli:measure` QName before resolving, per the XBRL spec's
  `collapse` whitespace facet. A padded measure like `"  iso4217:USD  "`
  now emits `Unit = "USD"` instead of `"USD  "`, matching
  `InlineXbrlParser.ResolveUnit`. Fixes silent value-column
  misclassification in downstream FinancialFacts tools when the two
  parsers disagreed on the same logical input.
- `StripPrefix` / `ResolveUnit` drop XBRL facts whose measure has an
  empty local name (e.g. `iso4217:`). Previously the fact ended up in
  the output with `Unit = ""`, which is unusable downstream.

#### Web / accessibility

- AJAX modal titles use `<h2>` to keep the page heading hierarchy intact
  (was an `<h5>`, producing a heading-level skip from the navbar `<h1>`).
- MCP-client accordion radio buttons, the worker status page search box,
  and the status page auto-refresh toggle now carry `aria-label`s for
  screen readers.
- The site navbar is wrapped in a `<header>` landmark so assistive tech
  can jump to it directly.
- The home-page CTA row wraps on narrow viewports instead of overflowing
  horizontally.

## [1.1.0] — 2026-05-22

### Added

#### Holdings / 13F

- Near-real-time 13F-HR ingestion — picks up new 13F filings shortly after
  publication alongside the existing quarterly bulk backfill, so the holdings
  side of the portal catches up to recent filings within minutes instead of
  waiting for the next bulk window.
- `/Holdings/Activity` — market-wide quarterly leaderboards (Top Buys / Top
  Sells / New Positions / Sold-out Positions) for any 13F quarter, with a CSV
  export covering all four boards.
- `/Holdings/MostHeld` — 13F breadth ranking. Stocks ranked by number of
  institutional filers reporting them, with quarter-over-quarter delta filers,
  total reported value, delta value, and percentage of the 13F universe. 100
  rows per page, sortable by filers / Δ filers / value.
- `/Holdings/Screener` — cross-sectional holdings screener with criteria
  (filer count, delta filer count, total value, delta value, percent of
  float, new / sold-out positions, industry), CSV export, and configurable
  comparison quarter.
- `/Institutions` — browsable, searchable index of every 13F filer with
  per-filer position count and total dollar value at the latest 13F quarter.
  Sort by name, position count desc, or value desc.
- `/Institutions/Compare` — side-by-side overlap view between two filers,
  with Jaccard and dollar-weighted overlap metrics and a per-stock comparison
  table.
- `/Institutions/Combined` — consensus-portfolio view across 2-25 filers,
  ranking stocks by how many of the selected funds hold them and combined
  dollar value.
- `/Institutions/{cik}/Backtest` — institution holdings backtest with a
  Chart.js cumulative-return chart against a benchmark (default S&P 500),
  rebalance trail, and CAGR / max-drawdown stats.
- Institution profile redesign — portfolio summary header (reported AUM,
  position count, top-10 / top-25 concentration, QoQ turnover, quarters
  reported), industry / sector allocation card, and a Latest Quarter
  Activity section grouping holdings into Initiated / Increased / Reduced /
  Exited buckets.
- Stock Holdings tab — quarterly position-change grouping (Initiated /
  Increased / Reduced / Exited / Unchanged) plus Top Buyers / Top Sellers
  cards, with negative deltas rendered consistently as `-$X` (not `$-X`).
- Per-stock holders CSV download, institution portfolio CSV download, and
  market-wide activity CSV downloads.

#### MCP tools (Holdings)

- `GetTopBuyersSellers` — institutions that moved the most on a stock
  quarter over quarter.
- `GetMarketWide13FActivity` — universe-wide buys / sells / new / sold-out
  leaderboards for a quarter.
- `GetMostHeldStocks` — cross-sectional 13F breadth ranking.
- `GetInstitutionSummary` — AUM, positions, top-N concentration, QoQ
  turnover for one filer.
- `GetInstitutionSectorAllocation` — portfolio grouped by industry /
  sector with percent of portfolio.
- `GetInstitutionQuarterlyActivity` — Initiated / Increased / Reduced /
  Exited buckets per filer.
- `GetFundOverlap` — Jaccard and dollar-weighted overlap between two
  funds.
- `GetConsensusHoldings` — combined picks across 2-25 funds with a
  `minFunds` threshold.

#### SEC Financial Facts (XBRL)

- SEC Company Facts API ingestion — every reported XBRL fact for every
  CIK, persisted into `FinancialFact` with per-period identity.
- Standalone and Inline XBRL parsers for dimensional fact extraction, plus
  a new `FinancialFactDimension` schema for downstream dimensional queries.
- "Financial Statements" tab on the stock detail page rendering Income
  Statement / Balance Sheet / Cash Flow with period selectors.
- MCP `GetFinancialStatement` — full statement for one company / period.
- MCP `GetFinancialFact` — concept time series for one company.
- MCP `CompareFinancialFact` — peer comparison across multiple tickers.
- Fiscal year-end detection for companies. The SEC document scraper now
  reads EDGAR's submissions `fiscalYearEnd` field and persists
  `FiscalYearEndMonth` / `FiscalYearEndDay` on `CommonStock` (sourced
  entirely from public SEC data, no extra request on the common path —
  the metadata fetch primes the submissions cache reused by the filings
  fetch). A new `FiscalCalendar` helper maps any date to a company's
  fiscal quarter/year, so off-calendar filers (e.g. Apple ≈ September,
  Microsoft = June) are no longer misrepresented by calendar-quarter
  math.

#### Stock metadata

- Sector taxonomy + `Industry.SectorId` foreign key.
- Yahoo `assetProfile` ingestion — populates Industry, Sector, market
  capitalisation, and the company description on `CommonStock`.
- ALL-CAPS company names from the SEC EDGAR feed are now title-cased on
  ingest (so "APPLE INC" displays as "Apple Inc").

#### Technical indicators (Yahoo prices)

- Stochastic Oscillator (`%K` / `%D`).
- Average True Range (ATR).
- On-Balance Volume (OBV).
- All three exposed via MCP alongside the existing SMA / RSI / MACD.

#### Global search

- Redesigned global search with a filter sidebar, category scope filter,
  date-range filter, sort (relevance / name), result-count summary, and a
  visible clear button.
- Instant as-you-type results with keyboard navigation; `/` shortcut
  focuses the navbar search; a recoverable empty state when a category
  filter hides every hit.
- Search now resolves institution, insider, and congress-member profiles
  in addition to stocks and filings.

#### Web portal

- Navbar reshuffle — `Home · Stocks · Institutions · More ▾ · MCP ·
  Status`. The `More` dropdown groups Economic Data, Futures, and Market
  so the row stays readable on medium viewports; the mobile menu keeps a
  flat list.
- Stocks browser — sort selector and minimum-market-cap filter, with
  filters carried across pagination links.
- Live activity feed at `/Status/Activity` rendering a scraper SSE
  stream (paused / resumed via a toggle, screen-reader live region).
- Skip-to-main-content link in the shared layout.
- Many accessibility improvements: table headers + accessible names on
  every data table, ARIA labels on icon-only buttons, corrected heading
  hierarchy across stock / institution / status / login / market /
  EconomicData / Cftc pages, `role=img` + `aria-label` on chart canvases,
  search-form `role=search` landmarks, labelled mobile-nav hamburger,
  rel=noopener + aria-label on external GitHub links.
- Status page Live activity entry-point is now a solid green pill with a
  pulsing indicator (was a near-invisible ghost button).
- `/Home/Connect` copy-to-clipboard buttons now actually copy.
- Web portal checks GitHub Releases and shows a banner when a newer
  version is available, displaying the new and current versions with
  links to the "Updating" guide and an in-app rendered changelog. The
  check is cached, runs off the request path, fails silently, and can be
  disabled with the `CHECK_FOR_UPDATES` environment variable (default
  `true`).

#### Messaging infrastructure

- MassTransit on Postgres SQL transport with the EF outbox pattern,
  composed in every host (web / MCP / worker) and test fixture.
- `StockCusipChanged` event published from `CommonStockManager.SetCusip`
  and consumed by the Holdings scraper to invalidate `ProcessedDataSet`
  rows and immediately re-process pending 13F data sets, instead of
  waiting up to 24 hours for the next worker cycle.
- `ScraperActivity` contract published by every scraper, consumed by the
  web's in-process broadcaster and surfaced on the live activity feed.

### Fixed

#### Holdings ingestion

- The Yahoo, FTD, and FinancialFacts scrapers now guard against the
  parent `CommonStock` disappearing between the per-cycle ticker-map
  read and the per-batch write. Without the guard, a cold-start tick
  alongside CompanySync trips `FK_*_CommonStock_CommonStockId` repeatedly
  and the activity feed fills with errors; the scrapers now skip stale
  IDs gracefully and log a warning instead of dropping rows for
  surviving stocks alongside the orphan.
- The Holdings worker now wakes on `StockCusipChanged` instead of
  waiting for the next ≤24h cycle, and retries a tracked-CUSIP miss
  instead of permanently marking the data set as processed.
- Pre-2021 FTD ZIP files (`cnsfails20*`) routinely 404 because SEC moved
  their archive. The handler already logs these as a warning, but used
  to also dump the `HttpRequestException`'s stack trace, making
  cold-start logs look full of unhandled errors. The stack trace is now
  suppressed — the warning line carries the only useful signal.

#### SEC / XBRL

- `FinancialFactsImportService` was reading SEC's filing-level `fy` /
  `fp` fields as each fact's period identity, but SEC stamps every
  comparable-year value inside one filing with the **filing's** fiscal
  year (a FY2024 10-K carrying three years of revenue tags all three
  rows `fy=2024, fp=FY`). The resulting collision at the natural unique
  index made downstream consumers filter ambiguously by `(FiscalYear,
  FiscalPeriod)`, surfacing wrong figures on the web Financials tab,
  MCP `GetFinancialStatement` / `CompareFinancialFact`, and the MCP
  `GetFinancialFact` time series. `FiscalYear` / `FiscalPeriod` are now
  derived from `PeriodStart` / `PeriodEnd` against
  `CommonStock.FiscalYearEndMonth` / `FiscalYearEndDay` via a new
  `FiscalPeriodResolver` (handles 52/53-week filer drift, leap-year FYE
  clamps, half-year and nine-month cumulatives, and falls back to the
  pre-fix behaviour when the company's FYE is unknown). **Existing
  `FinancialFact` rows in any deployed database still carry the pre-fix
  identity** — to refresh, stop the worker, drop the `FinancialFact`
  and `FinancialFactsSyncStatus` tables, and restart; the scraper will
  re-ingest from the SEC HTTP cache without re-downloading.
- iXBRL facts whose scale would overflow `decimal` are now dropped
  instead of failing the import.
- BM25 chunk-search SQL bounded with a 5s command timeout.
- Network failures during document fetching are now retried (transient)
  rather than recorded as errors; expected legacy/malformed ownership
  XML no longer surfaces as a reported error.

#### Yahoo

- Incomplete OHLC rows are skipped instead of emitting impossible bars;
  `AdjustedClose` falls back to `Close` when Yahoo returns a null hole.
- Bars are dated on the exchange-local calendar via `meta.gmtoffset`
  instead of UTC, fixing one-day drift on non-US tickers.
- Yahoo column access is bounded for ragged chart payloads.
- 429 and 5xx responses are retried with backoff across the Yahoo,
  SEC, FINRA, House, and Senate clients.

#### Web / culture

- All date parsers (FRED observations, SEC filings, Form 4 transaction
  date, congress disclosures, FTD file names, holdings ISO dates) now
  use `InvariantCulture` to keep imports deterministic on machines with
  comma-decimal locales.
- `StocksController.Index` clamps non-positive `page` to 1 (was a 500
  from a negative `OFFSET`).
- `HomeController.Error` clamps the status code to the valid HTTP
  range.
- `StatisticsExtensions.SafeRound` returns null for out-of-decimal-range
  doubles.
- `Equibles.Web.ComputeRsi` returns 100 for a zero-loss window (was
  NaN).
- The home-page title no longer duplicates the Equibles brand.
- `CsvExportService.Format(DateOnly)` pinned to invariant culture.
- `HoldingsExportController.Activity` returns 404 when the selected
  date has no prior quarter.

#### Operator UX

- Embedding service healthcheck used `curl`, which is not present in
  the `ollama/ollama` image, leaving the container permanently
  `unhealthy` and preventing `embedding-pull` / `worker-embedding` (and
  thus the entire vector embedding profile) from starting. Switched the
  probe to `ollama list`.
- `EmbeddingConfig` is now properly bound on startup, so the embedding
  worker actually runs when the profile is enabled.
- Embedding chunk failures are isolated per chunk; systemic outages
  back off instead of looping.
- Worker now skips and retries soon when the tracked universe is empty
  at cold start, instead of repeatedly attempting empty cycles.
- `Sec:ContactEmail` is rejected when whitespace-only.
- Live activity feed shows newest events at the top of the list.

### Security

- Markdown link URI scheme allow-list — `javascript:` (and other
  non-http(s) schemes) in user-controlled markdown are now stripped
  before render.
- `ChunkingStrategy.CleanText` strips `<script>` / `<style>` / comment
  nodes before chunking, blocking script injection through document
  text.
- `FileManager.SaveFile` enforces an `AcceptedExtensions` allow-list.
- `IsValidDisclosureUrl` uses an origin-based check instead of
  substring matching, closing an SSRF bypass on Congress disclosure
  URLs.
- `IsSafeFilename` switched to a bare-name allow-list.
- `DocumentTextTools.HighlightKeyword` guards against empty-keyword
  loops (DoS).
- `ErrorManager.Create` uses surrogate-pair-safe truncation.
- Search query control characters are stripped before being written to
  application logs.
- Bcl.Memory transitive dependency upgraded to remediate
  CVE-2024-43485.

## [1.0.0] — 2026-05-15

First tagged release.

### Added

- SEC filings ingestion with full-text search — 10-K, 10-Q, 8-K from SEC EDGAR.
- Institutional holdings (SEC 13F-HR) — top holders, ownership history, institution portfolios.
- Insider trading (SEC Form 3/4) — director, officer, and 10% owner transactions.
- Congressional trading from House and Senate disclosures.
- Short data — fails-to-deliver (SEC), daily short volume and short interest (FINRA).
- Economic indicators from FRED (Federal Reserve).
- Futures positioning — CFTC Commitments of Traders for 30+ contracts.
- Market indicators — CBOE VIX history and put/call ratios.
- Daily stock prices with technical indicators (SMA, RSI, MACD) from Yahoo Finance.
- MCP server exposing all data as tools for Claude, ChatGPT, Cursor, and other MCP-compatible clients.
- Web portal for browsing stocks, holdings, filings, economy, futures, and market data.
- Background worker — scrapers and document processor.
- Docker Compose stack (ParadeDB + web + MCP + worker), with an optional vector-embedding profile.

[Unreleased]: https://github.com/daniel3303/Equibles/compare/v1.1.1...HEAD
[1.1.1]: https://github.com/daniel3303/Equibles/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/daniel3303/Equibles/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/daniel3303/Equibles/releases/tag/v1.0.0
