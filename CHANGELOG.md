# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
