# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Government contracts — the backfill no longer freezes when USAspending has one of its intermittent bad spells. The import now persists a resumable scan checkpoint (the last fully-completed action-date window) instead of resuming from `MAX(ActionDate)`, so a transport failure that aborts a cycle resumes where it left off rather than restarting the whole range and re-flooding the error log; once caught up it re-scans a trailing lookback window each cycle so late-published awards are not permanently skipped. PR #4213. The scan window was also narrowed from 7 days to 2 (each window fires far fewer API requests, so it completes through the API's flakiness rather than aborting) and the poll interval tightened from 24h to 3h, so new awards surface within hours once the backfill catches up. PR #4214. The window was then narrowed to 1 day and the API client's per-page retry patience raised (6 → 8 attempts, ~2min → ~8.5min) so a single flaky page rides out a spell instead of aborting the whole window. PR #4215. The true failure mode was then run to ground: some backends behind USAspending's IPv4 load balancer accept a connection and close it without a response, and a pooled connection pins the client to its drawn backend so the whole retry ladder dies on a sick one — every request now goes out on a fresh connection (Connection: close), making each retry an independent backend draw, carries a User-Agent, and the retry ladder returns to six attempts. PR #4216.

## [1.4.0] — 2026-07-03

### Added

- Filesystem-backed blob storage — binary file content (zipped XBRL envelopes, as-filed HTML, 8-K exhibit images, filing text bodies) can now be stored on a content-addressed, sharded filesystem tree (`<root>/<tier>/sha256/<aa>/<bb>/<hash>`) instead of inline in the database, with byte-level deduplication and crash-safe writes (temp → fsync → atomic rename → fsync dir). A `FileBackfill` drain worker migrates existing database blobs to disk continuously and memory-bounded, then self-disables. Reads dispatch transparently through `IFileManager.GetContent`/`OpenRead` on the provider recorded per row, so old (database) and new (filesystem) rows both stay readable. Opt-in via the `FileStorage` config section (off by default — existing installs keep database blobs until they enable the store and run the backfill). PRs #4050, #4052, #4053, #4061, #4062, #4063.
- Filesystem-only write chokepoint — with the store enabled, `FileStorageRouter` is the single decision point for where new bytes land: the filesystem, always. No code path (and no API) persists new blob bytes in the database while the store is on; it falls back to the database only when the store is disabled. PR #4086.
- Staged, reversible blob deletion — deleting a file queues its content hash and a daily sweep retires unreferenced blobs through a reversible trash phase (grace window → move to `.trash` → restore if re-referenced → purge), backed by a rolling weekly disk-vs-database reconciliation that catches deletions the queue cannot see (cascades, direct deletes). Off by default via `BlobSweep`. PR #4077.
- Corporate actions module — captures historical stock splits and cash dividends from the Yahoo chart-events payload into new `StockSplit`/dividend entities, with one-time historical backfills and a split-adjustment factor helper. MCP holdings, short-interest and insider tools (and the short-squeeze score) are now split-adjusted so cross-sectional comparisons hold across split boundaries. PRs #4048, #4049, #4066, #4067, #4071.
- As-reported financial statements — the worker captures EDGAR R-files, parses them into as-reported statement models (income statement, balance sheet, cash flow) with a richer statement catalog and tag variants, persists filer-extension (company-specific) XBRL concepts, and surfaces financial-sector top lines on the income statement. PRs #4083, #4088, #4899, and the financial-facts series.
- FDA catalysts module — ingests and parses the FDA.gov advisory-committee calendar and exposes upcoming catalysts through the `GetFdaCatalysts` MCP tool.
- Government contracts module — a USAspending federal-contracts module surfacing awards data.
- Fund directory (SEC Form NPORT-P) — materialises a `FundSeries` directory from NPORT-P, adds series-scoped NPORT queries and fund-directory MCP tools, and discovers NPORT-P filings from multi-series fund trusts. Plus a `GetFundCloneBacktest` MCP tool that backtests cloning a single filer's 13F portfolio.
- As-filed 8-K viewer — builds an as-filed HTML rendering of 8-K filings with their exhibits, downloads and stores exhibit images, and backfills `Document.Items` onto historical 8-Ks from the submissions feed.
- Hybrid SEC search — BM25 + semantic (vector) retrieval with reciprocal-rank fusion and a pluggable embedding provider. PRs #4930–#4940.
- Website & investor-relations discovery — a discovery worker fed by prioritised sources (SEC filings first, then Wikidata joined on CIK, then the Yahoo asset profile), probing IR pages over plain HTTP first and falling back to a stealth-browser sidecar for bot-walled sites, with version-gated re-probes and exponential backoff. Refs #3683, #3700.
- Per-host outbound rate limiting with a Cloudflare-1015 cooldown for scrapers, and finer market-close polling for FINRA short volume.
- Miscellaneous: Form 4 Rule 10b5-1 checkbox capture; SEC cover-page shares-outstanding (summed per share class for multi-class issuers); in-place document content replacement that keeps the document id; new `Authentication`, `WebRequest` and Alvis error sources; a frosted-glass sticky navbar.

### Changed

- Investor-relations discovery and IR news/events content were extracted out of the open-source tree into the commercial tier; the retired `CommonStock` IR-discovery columns were dropped. PRs #4078, #4079, #4096.
- 13F reconciliation now runs on demand rather than via a dedicated worker, sharing the backfill work-set through `DocumentRepository`; the one-time XBRL backfill worker was likewise removed in favour of an adaptive-cadence drain. Closes #3741.
- SEC ingest, XBRL capture and R-file capture were parallelised and the SEC request rate raised to 5/s; NPORT holdings and document ingestion-stats scans gained covering indexes.

### Fixed

- Holdings/13F correctness — per-stock and market-wide activity windows are computed over Form 13F only (13D/G daily-dated rows no longer pollute quarter math); Schedule 13D/G stakes are excluded from portfolio summaries, fund scoring and the smart-money index; submissions are de-duplicated by parsed filing date; cross-type amendments no longer wipe 13F holdings (with self-healing gaps); and share-value casts are range-checked against corrupt oversized rows. Closes #3732, #3738, #3685.
- Financial facts — `GetFinancialFact` returns the discrete quarter (not YTD) and the currently-reported instant; early-January fiscal-year-ends are treated as December-anchored; the revenue alias reaches back past ASC 606 via `SalesRevenueNet`; additional iXBRL number formats (zerodash, hyphenated comma-decimal) are parsed; and decimal→long share-count casts are range-checked. Closes #3836.
- Yahoo pricing & market cap — market capitalisation is reconciled onto the authoritative EDGAR ordinary-share base (no longer inflating foreign-issuer caps), recomputed from EDGAR shares × price when Yahoo's own value is unusable; the in-progress trading day is no longer stored as a settled close; non-positive OHLC bars are skipped; and the daily price sync crawls stalest-first so interrupted cycles stop starving tail stocks.
- SEC — the backfill frontier is preserved across empty full rescans; all pending XBRL backfill drains regardless of the live-scraper sync floor; oversized stitched as-filed HTML is dropped at capture with hardened attribute escaping; chunking separates adjacent block-element text and tokenises Part/Item headings on em/en-dash separators; Form D investor counts are clamped before narrowing to int. Closes #3839, #3842, #3866.
- Numerous congress, government-contracts, insider-trading, common-stocks, CBOE, FINRA and worker fixes; see the commit history for the full list.

## [1.3.0] — 2026-06-11

### Added

- Explanatory intro on every stock data tab — each tab on the stock page (price, holdings, short interest, short volume, fails-to-deliver, financials, SEC documents, insider trading, proposed sales, fund holdings, fund operations, exempt offerings, congressional trades) now opens with a short description of what the data is and where it comes from, rendered via a shared `_TabIntro` partial. PR #3165.
- Investment advisers (SEC Form ADV) — browse SEC-registered investment advisers at `/advisers` (name search, ranked largest-by-assets, paginated) with a per-firm profile at `/advisers/{crd}` showing regulatory assets under management (discretionary / non-discretionary / total), main office, employee count and fee structure. Data comes from the SEC's bulk Form ADV download, refreshed monthly by a background worker and keyed by Organization CRD number. Two MCP tools expose the same data to assistants: `SearchInvestmentAdvisers` (by name) and `GetInvestmentAdviser` (full profile by CRD). Issue #1866 (PRs #2867, #2868, #2869, #2870, #2871).
- Fund portfolio holdings (SEC Form NPORT) — parses funds' monthly portfolio filings in the worker, exposes a fund's holdings via MCP, and adds a "Fund holdings" tab to the stock page listing the largest positions with value and percentage of net assets (capped, showing the largest N of M). Issue #1868 (PRs #2860, #2861, #2862, #2863).
- Registered-fund annual operations (SEC Form N-CEN) — parses N-CEN filings in the worker, exposes them via MCP, and adds a "Fund operations" tab to the stock page surfacing each tracked fund/ETF's service providers (auditor, custodian, transfer agent, principal underwriter, etc.). Issue #1869 (PRs #2856, #2857, #2858, #2859).
- Form D exempt offerings (private placements) — parses Form D notices in the worker, exposes them via the `GetExemptOfferings` MCP tool, and adds an "Exempt offerings" tab to the stock page with offering amount, amount sold, minimum investment, investor count and claimed Regulation D exemptions (offering amounts reported as "Indefinite" are preserved). Issue #1867 (PRs #2851, #2852, #2853, #2855).
- Form 144 proposed insider sales — parses Form 144 notices (an insider's intent to sell restricted or control stock) in the worker, exposes them via the `GetProposedSales` MCP tool, and adds a "Proposed sales" tab to the stock page showing seller, relationship, shares, aggregate market value and approximate sale date. Issue #1865 (PRs #2846, #2847, #2849, #2850).
- Raw XBRL filing artifacts — the worker now captures and stores the original iXBRL/XBRL document for each financial filing on ingest, preserving the machine-readable source alongside the parsed facts. PRs #2864, #2865.
- Fund scoring — a scoring engine rates 13F filers on risk-adjusted performance (alpha versus a benchmark), recomputed by a background worker, with scores surfaced and sortable in the institutions portal. PRs #2837, #2838, #2839, #2841.
- Smart Money Index page in the institutions portal — aggregates the highest-scoring funds into a single consensus signal. PRs #2843, #2845.
- Bollinger Bands on the stock price chart and via a `GetBollingerBands` MCP tool, computed by the technical-indicator service. PRs #2833, #2834, #2835.
- Moving-average cross (golden/death cross) and consecutive up/down price-streak detection, shown as badges on the stock Technicals tab. PRs #2827, #2828.
- Price performance versus SPY on the stock price tab — trailing and calendar-window returns benchmarked against the S&P 500. PRs #2825, #2826.
- Location and AUM / position-count range filters on the institutions index. PRs #2823, #2824.
- Global search results are now sorted by date, most recent first. PR #2842.
- Contributor License Agreement (`CLA.md`) based on Project Harmony HA-CLA-I-ANY 1.0. Contributors must sign via CLA Assistant before pull requests can be merged. Enables Equibles to remain AGPL-3.0 while sharing code with the commercial offering.
- Optional `configureOptions` callback on `AddEquiblesDbContext<TContext>` (applied after the standard Npgsql + lazy-loading setup) so a host can adjust context-level `DbContextOptions` — e.g. suppress a specific warning — without replacing the registration helper. Default behavior unchanged. PR #2279.
- Most-shorted stocks leaderboard at `/most-shorted` — ranks stocks by FINRA short interest for a selected bi-monthly settlement date (current short position, change vs. previous, days to cover, average daily volume), with a settlement-date selector, server-side sort, and pagination; each row links to the per-stock short-interest tab. Issue #2536 (PR #2755).
- Largest short volume page at `/short-volume` — ranks stocks by FINRA consolidated daily short volume for a selected trading day, with a trading-day selector, sort, and pagination. Issue #2644 (PR #2753).
- `GetLargestShortVolume` MCP tool — market-wide ranking of the largest daily short volume for a trading day (defaults to the latest available). PR #2646.
- Per-quarter 13F aggregate snapshot tables, with background rebuild + drain workers and first-boot backfill, backing the holdings stats/trends pages. PRs #2463, #2468, #2473, #2476.
- Segmented stock-section navbar with summary metrics on the Technicals tab. PR #2534.
- Short-volume-percentage line overlaid on the per-stock short volume chart. PR #2533.
- Typeahead institution picker on the overlap matrix page. PR #2524.
- Exact-ticker search submissions now redirect straight to the stock page. PR #2529.
- `Cmd`/`Ctrl`+`K` global search shortcut (replacing `/`) to match the commercial portal. PR #2510.
- Insider-trading per-share prices are cross-checked against Yahoo and flagged when implausible.
- Schedule 13D/13G beneficial-ownership filings — parses 13D/13G XML, ingests filings from the EDGAR daily index plus a realtime sweep, and adds a filing-type filter to the institutional holders table so activist/passive stakes can be isolated. PRs #3566, #3567, #3568, with parser robustness follow-ups (PRs #3580, #3611, #3617, #3626, #3678, #3679).
- Off-exchange (OTC/ATS) volume — scrapes FINRA's OTC Transparency weekly volume data and exposes it via the `GetOffExchangeVolume` MCP tool. PRs #3569, #3570, #3571.
- Investor relations news and events — the worker discovers each stock's IR page URL, classifies the IR platform, and scrapes news, events, and the earnings calendar from Nasdaq IR Insight and Q4 Inc sites (with a per-stock discovery cooldown); `GetInvestorRelationsNews` and `GetInvestorRelationsEvents` MCP tools expose the data. PRs #3572, #3574, #3595, #3596, #3598, #3600, #3601, #3604, #3608.
- Dimensional XBRL facts — financial facts now carry a dimensions key, an extraction service sweeps captured XBRL envelopes into dimensional facts, and the `GetRevenueBreakdown` MCP tool surfaces segment/product/geography revenue splits. PRs #3613, #3614, #3615, #3621, #3629.
- Composite short-squeeze score — rates stocks from stored short data (short interest, days to cover, short-volume ratio, fails-to-deliver), exposed via the `GetShortSqueezeScores` MCP tool. PRs #3623, #3624.
- Reverse fund-ownership lookup — the `GetFundsHoldingStock` MCP tool lists the funds holding a given stock from NPORT portfolio data, backed by a new CUSIP index and reverse-lookup queries. PRs #3610, #3625.
- Per-holder quarterly AUM snapshots materialised by the snapshot worker, backing faster institution history reads. PR #3628.
- Institutional ownership trend chart on the stock holdings tab — per-quarter holder count and aggregate position trend. PRs #3634, #3635.
- FRED economic release calendar — imported during the scraper cycle and exposed via the `GetEconomicCalendar` MCP tool. PRs #3647, #3648, #3649.
- Congressional annual financial disclosures — parses House Clerk annual reports (Form A) with column-aware schedule extraction and Senate eFD annual reports, rolls assets and liabilities up into net-worth bands, and exposes them via the `GetMemberNetWorth` MCP tool. PRs #3653, #3655, #3658, #3662, #3663.
- DEF 14A proxy statements — new document type included in the scraper form set, with multi-word form types accepted by the HTML normalizer. PRs #3671, #3672.
- 8-K item numbers are captured on ingest and saved documents are announced on the bus. PR #3575.
- Percent-of-class on institutional holdings, parsed from 13D/G cover pages. PR #3565.
- NPORT-P filings are reprocessed to backfill fund holdings ingested before the parser landed. PR #3498.

### Changed

- **Multi-context module system.** Split `EquiblesDbContext` into an abstract `EquiblesDbContextBase` (module iteration, no Postgres extensions) plus a concrete `EquiblesFinancialDbContext` (enables pgvector; ParadeDB stays in the Npgsql options). Renamed `EquiblesDbContext` → `EquiblesFinancialDbContext`. Added `IFinancialModule` / `ICustomerModule` markers so a host can scan for either domain; every OSS module implements `IFinancialModule`. `BaseRepository` is now generic over the context (`BaseRepository<TEntity, TContext>`) with a one-arg shim binding to the financial context, so existing repositories are unchanged. `AddEquiblesDbContext` is generic over the context with a per-context `ModuleConfigurationSet<TContext>` (no shared module list); added the `AddEquiblesFinancialDbContext` convenience overload. Unlocks deployments running a second context (e.g. a customer database) over the same module system. PR #2258.
- **No transactional outbox in OSS standalone.** `AddMessaging` no longer registers the EF outbox and gains a `configureBus` hook so a host can opt a context into one. OSS consumers must therefore be idempotent (no inbox/dedup ships). `CommonStockManager.SetCusip` now publishes **after** `SaveChanges` to avoid phantom events on rollback. PR #2258.
- **Holdings snapshot rebuilds are now throttled and coalesced.** `Filings13FImportedConsumer` no longer rebuilds the AUM/sector snapshots inline — it marks the affected quarter dirty via a new `DirtyAt` timestamp on `AumQuarterlySnapshot`. A new `AumSnapshotDrainWorker` ticks every 5 minutes and rebuilds any quarter whose dirty flag has been set for more than an hour, using optimistic-concurrency clear so a consumer event landing mid-rebuild isn't lost. During 13F filing-season burst windows hundreds of imports per day for the same quarter now produce one rebuild per cooldown window instead of one per import. The daily safety-net `AumSnapshotRebuildWorker` is narrowed to the four most recent quarters (older quarters are effectively frozen — amendments trigger their own consumer event). First-boot backfill of every quarter is unchanged.
- Holdings stats and trends pages (`/holdings/stats`, `/holdings/trends`) now read the per-quarter snapshots instead of recomputing aggregates on each request. PR #2479.
- Per-stock quarterly 13F activity backing the conviction heat map is now materialized by the snapshot worker instead of recomputed per request, so the heat map renders from precomputed rows. PR #3238.
- Stock data tabs clamp their history to the configured minimum sync date (`Worker__MinSyncDate`), so price, short-data, holdings, insider, and congressional views never show partially-backfilled periods. PRs #3638, #3639, #3640.
- 13F and NPORT data-set processing is version-stamped, so rows imported by an older parser are reprocessed automatically after a parser fix instead of staying wrong until a manual backfill.
- SEC rate-limit handling — a 429 now pauses all SEC requests for the full block window, in-flight successes no longer produce a false "cleared" signal, and block/clear transitions publish bus events for observability.
- The historical XBRL backfill worker now defaults off — the one-time sweep of legacy documents has drained; new filings capture their XBRL on ingest. PR #3622.

### Fixed

- Insider-trading values for companies listed via American Depositary Shares (ADS/ADR) are no longer overstated. These Form 4 filings report the share count in the issuer's underlying ordinary shares but quote the price per ADS, so the dashboard's shares × price read far too high — e.g. SaverOne (SVRE) appeared as an ~$8.6B insider buy when the real value was ~$200K. The price is now restated to a per-ordinary basis (from the filing's footnotes) whenever the share count is an exact multiple of the ADS ratio, leaving the as-filed price preserved. Existing rows are corrected on the next filing reprocess.
- XBRL financial values wrapped in parentheses **and** carrying an explicit `sign="-"` are no longer double-negated — a parenthesised negative now resolves to a single negative value. PR #2819.
- Financial tables with merged `rowspan`/`colspan` cells are expanded to a rectangular grid before parsing, so values land in the correct row and column. PR #2820.
- CFTC Commitments-of-Traders ingestion looks up CSV columns by the headers cftc.gov actually ships rather than by fixed position, so a column reorder no longer mismaps positioning data. PR #2478.
- Short-interest pages (the `/most-shorted` leaderboard and the per-stock settlement-date selector) load faster: `ShortInterestRepository.GetAllSettlementDates` now walks the distinct settlement dates with a recursive loose index scan instead of `SELECT DISTINCT`, which scanned the whole `SettlementDate` index on every request and spiked on a cold buffer cache. Falls back to the `DISTINCT` query on non-relational providers.
- `CommonStockManager.SetCusip` publishes `StockCusipChanged` via the root `IBus` instead of the scoped `IPublishEndpoint`. A commercial host that enables a bus outbox on a different DbContext (the customer database) would otherwise capture this publish into that context and never deliver it — the flow only saves the financial context. Tests updated to substitute `IBus`. PR #2271.
- Culture-invariance hardening — MCP tool tables, web date/number formatting, SEC/CFTC/FINRA date parsing, and worker interval logging now consistently use the invariant culture, so output and parsing no longer depend on the server locale. Numerous PRs (e.g. #2748, #2735, #2724, #2705, #2661, #2464, #2460).
- Ticker normalization uses `ToUpperInvariant` across the web `StocksController` and the Holdings / InsiderTrading MCP resolvers. PRs #2609, #2517.
- Version comparison handles `v`-prefixed tags so the new-version banner doesn't misfire. PR #2621.
- CBOE put/call ingestion switched to the daily-page scraper and now persists incrementally so a cold start populates. PRs #2526, #2751.
- House congressional PTR PDFs are parsed via page geometry so Representatives land in the right columns. PR #2530.
- SEC Form 4 transaction-code mapping corrected (I/W). PR #2469.
- Investment-adviser name search treats LIKE wildcards (`%`, `_`) in the query as literal characters, so a search containing them no longer matches unintended advisers. Issue #2905 (PR #3010).
- The FINRA daily short-volume scraper now guards against the parent `CommonStock` disappearing between the per-cycle ticker-map read and the per-batch write — the same guard already applied to the Yahoo, FTD, and FinancialFacts scrapers. A cold-start tick alongside `CompanySyncService` could otherwise trip `FK_*_CommonStock_CommonStockId` and fail the entire insert batch (poisoning rows for surviving stocks too); stale IDs are now re-validated against the live `CommonStock` table per batch and dropped with a warning so the surviving rows still persist. Issue #3288 (follow-up to #1591).
- Form 4 insider trades are attributed to the issuer of the traded security instead of the reporting owner's own ticker, and the insider dashboard no longer inflates totals with derivative prices or chain-duplicated filings. PRs #3500, #3502.
- 13F filings that duplicate the position value into the share-count column are repaired on import. PR #3499.
- SEC document normalizer no longer promotes "Part … of …" / "Item … of …" prose sentences to headings (including after page breaks), unwraps spaced inline content divs, and matches list-item wrapper classes as whole tokens. PRs #3484, #3487, #3488, #3491, #3531, #3532, #3553.
- MCP tools clamp client-supplied `maxResults` so out-of-range values can no longer produce a negative SQL `LIMIT` or oversized result sets. PRs #3540, #3541, #3544.
- MCP tool calls with a missing or wrongly-typed required argument return a structured "Invalid parameters" result naming the tool instead of an opaque internal error. PRs #3609, #3680.
- Fund Operations and Fund Holdings tabs are shown only for funds/ETFs. PR #3503.
- Backtest CAGR is no longer annualized for windows under 90 days, and the Double-Down Report excludes degenerate prior bases. PRs #3494, #3495.
- CBOE put/call ingestion stitches the chunked Next.js RSC payload back together before extracting the JSON, fixing empty imports after CBOE's site change. PR #3607.
- Senate eFD search and row dates are parsed culture-pinned, so congressional ingestion works on non-English-locale servers. Issues #3659, #3660 (PRs #3665, #3668).
- FTD feed symbols are resolved to class-share tickers when seeding CUSIPs, and EDGAR ticker rows without an exchange are skipped when parsing active companies. PRs #3616, #3618.
- Field truncation no longer splits UTF-16 surrogate pairs (filing fields and XBRL file names). PRs #3481, #3529.
- 13F filing summaries collapse to one row per accession before upsert, and FINRA's zero-volume days-to-cover sentinel is dropped from the squeeze score. PRs #3650, #3651.
- Filings whose `primaryDocDescription` is missing are kept instead of skipped. PR #3354.
- Container healthchecks give startup migrations time to complete before failing. PR #3492.

### Security

- Neutralized the `javascript:` scheme in Markdown autolinks. PR #2633.
- Baseline security response headers and HSTS on the web portal. PR #3542.

## [1.2.0] — 2026-05-26

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

[Unreleased]: https://github.com/daniel3303/Equibles/compare/v1.4.0...HEAD
[1.4.0]: https://github.com/daniel3303/Equibles/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/daniel3303/Equibles/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/daniel3303/Equibles/compare/v1.1.1...v1.2.0
[1.1.1]: https://github.com/daniel3303/Equibles/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/daniel3303/Equibles/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/daniel3303/Equibles/releases/tag/v1.0.0
