# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Fiscal year-end detection for companies. The SEC document scraper now reads
  EDGAR's submissions `fiscalYearEnd` field and persists `FiscalYearEndMonth` /
  `FiscalYearEndDay` on `CommonStock` (sourced entirely from public SEC data,
  no extra request on the common path — the metadata fetch primes the
  submissions cache reused by the filings fetch). A new `FiscalCalendar` helper
  maps any date to a company's fiscal quarter/year, so off-calendar filers
  (e.g. Apple ≈ September, Microsoft = June) are no longer misrepresented by
  calendar-quarter math.
- Web portal now checks GitHub Releases and shows a banner when a newer version
  is available, displaying the new and current versions with links to the
  "Updating" guide and an in-app rendered changelog. The check is cached, runs
  off the request path, fails silently, and can be disabled with the
  `CHECK_FOR_UPDATES` environment variable (default `true`).

### Fixed

- `FinancialFactsImportService` was reading SEC's filing-level `fy` / `fp`
  fields as each fact's period identity, but SEC stamps every comparable-year
  value inside one filing with the **filing's** fiscal year (a FY2024 10-K
  carrying three years of revenue tags all three rows `fy=2024, fp=FY`). The
  resulting collision at the natural unique index made downstream consumers
  filter ambiguously by `(FiscalYear, FiscalPeriod)`, surfacing wrong figures
  on the web Financials tab, MCP `GetFinancialStatement` /
  `CompareFinancialFact`, and the MCP `GetFinancialFact` time series.
  `FiscalYear` / `FiscalPeriod` are now derived from `PeriodStart` /
  `PeriodEnd` against `CommonStock.FiscalYearEndMonth` /
  `FiscalYearEndDay` via a new `FiscalPeriodResolver` (handles 52/53-week
  filer drift, leap-year FYE clamps, half-year and nine-month cumulatives,
  and falls back to the pre-fix behaviour when the company's FYE is unknown).
  **Existing `FinancialFact` rows in any deployed database still carry the
  pre-fix identity** — to refresh, stop the worker, drop the
  `FinancialFact` and `FinancialFactsSyncStatus` tables, and restart; the
  scraper will re-ingest from the SEC HTTP cache without re-downloading.
- Embedding service healthcheck used `curl`, which is not present in the
  `ollama/ollama` image, leaving the container permanently `unhealthy` and
  preventing `embedding-pull` / `worker-embedding` (and thus the entire vector
  embedding profile) from starting. Switched the probe to `ollama list`.

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

[Unreleased]: https://github.com/daniel3303/Equibles/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/daniel3303/Equibles/releases/tag/v1.0.0
