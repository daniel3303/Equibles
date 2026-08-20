# Equibles

[![CI](https://github.com/daniel3303/Equibles/actions/workflows/ci.yml/badge.svg)](https://github.com/daniel3303/Equibles/actions/workflows/ci.yml)
[![CodeQL](https://github.com/daniel3303/Equibles/actions/workflows/codeql.yml/badge.svg)](https://github.com/daniel3303/Equibles/actions/workflows/codeql.yml)
[![codecov](https://codecov.io/github/daniel3303/Equibles/graph/badge.svg?token=ggkiHVv92U)](https://codecov.io/github/daniel3303/Equibles)
[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?logo=docker&logoColor=white)](docker-compose.yml)
[![Self-Hosted](https://img.shields.io/badge/Self--Hosted-Ready-success?logo=serverless&logoColor=white)](docker-compose.yml)
[![MCP](https://img.shields.io/badge/MCP-Compatible-8A2BE2?logo=data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0id2hpdGUiPjxwYXRoIGQ9Ik0xMiAyQzYuNDggMiAyIDYuNDggMiAxMnM0LjQ4IDEwIDEwIDEwIDEwLTQuNDggMTAtMTBTMTcuNTIgMiAxMiAyem0wIDE4Yy00LjQyIDAtOC0zLjU4LTgtOHMzLjU4LTggOC04IDggMy41OCA4IDgtMy41OCA4LTggOHoiLz48L3N2Zz4=)](https://modelcontextprotocol.io)

**Equibles is a self-hosted, open-source financial data MCP server** — an open-source alternative to a Bloomberg Terminal, built for AI agents rather than humans. It scrapes, stores, and serves SEC filings and XBRL financials, 13F institutional holdings, insider and congressional trades, FINRA/SEC short data, FRED economic indicators, CFTC and CBOE positioning, fund filings, government contracts, and daily stock prices — and exposes all of it over the Model Context Protocol, so Claude, ChatGPT, Cursor, or any agent can query it directly. Runs on your own hardware with Docker, for free, forever.

**This is the open-source core of [Equibles](https://equibles.com).** [Equibles Cloud](https://equibles.com/mcp) runs this exact core and adds 44 more tools, 108 in total — earnings call transcripts and audio, real-time quotes, options chains with Greeks, LLM-extracted KPIs and guidance, buybacks, IPO filings, executive changes, valuation multiples, index composition, portfolio tracking, and a full US-market screener. Same protocol, same tool names, nothing to run — see [what's included](#whats-included).

> **Don't want to run anything?** Point your AI assistant at `https://mcp.equibles.com/mcp` and get a free API key at [equibles.com/mcp](https://equibles.com/mcp) — 100 requests/day, no card. Per-client setup guides live at [daniel3303/stock-market-mcp-server](https://github.com/daniel3303/stock-market-mcp-server).

See [`docs/`](docs/README.md) for the user guide and technical documentation.

## What's Included

Everything marked **Self-hosted** is scraped, stored, and served by this repo — **64 MCP tools**, no account, no key. [Equibles Cloud](https://equibles.com) runs this exact core over the same protocol with the same tool names, and adds 44 more tools, 108 in total.

| Domain | Data Source | Self-hosted | [Equibles Cloud](https://equibles.com) | Description |
|--------|------------|:---:|:---:|-------------|
| **SEC Filings** | SEC EDGAR | ✅ | ✅ | 10-K, 10-Q, 8-K annual/quarterly/current reports with full-text search |
| **Financial Statements** | SEC XBRL | ✅ | ✅ | Parsed income statement, balance sheet, and cash flow facts per fiscal period |
| **Holdings** | SEC 13F-HR | ✅ | ✅ | Institutional ownership — who owns what, how much, and trend over time |
| **Fund Filings** | SEC NPORT / N-CEN / Form D | ✅ | ✅ | Fund portfolio holdings, registered-fund operations (service providers), and exempt offerings (private placements) |
| **Investment Advisers** | SEC Form ADV | ✅ | ✅ | SEC-registered advisers — assets under management, main office, employee count, fee structure |
| **Insider Trading** | SEC Form 3/4/144 | ✅ | ✅ | Director, officer, and 10% owner transactions, plus proposed (Form 144) sales |
| **Congressional Trading** | House/Senate disclosures | ✅ | ✅ | Securities transactions by members of Congress, including the filed stock, option, bond, or other instrument |
| **Short Data** | SEC / FINRA | ✅ | ✅ | Fails-to-deliver (SEC), daily short volume and short interest (FINRA) |
| **Economic Indicators** | FRED (Federal Reserve) | ✅ | ✅ | Interest rates, inflation, employment, GDP, yield spreads, and more |
| **Stock Prices** | Yahoo Finance | ✅ | ✅ | Daily OHLCV prices with technical indicators (SMA, RSI, MACD) |
| **Futures Positioning** | CFTC | ✅ | ✅ | Commitments of Traders (COT) data for 30+ futures contracts |
| **Market Indicators** | CBOE | ✅ | ✅ | VIX volatility index (1990+) and put/call ratios by category |
| **Government Contracts** | USAspending.gov | ✅ | ✅ | Federal contract awards to public companies — amounts, awarding agencies, dates, and NAICS/PSC codes |
| **FDA Catalysts** | FDA.gov | ✅ | ✅ | Advisory-committee (AdComm) meeting calendar — scheduled FDA panel dates, center, and title that act as regulatory catalysts for biotech/pharma stocks |
| **Earnings Call Transcripts** | Company webcasts | — | ✅ | Full quarterly-call transcripts with speaker attribution, searchable alongside filings |
| **Earnings Call Audio** | Company webcasts | — | ✅ | The recorded call itself, playable and aligned to the transcript |
| **Earnings Briefs, Guidance & Narrative Shift** | Earnings calls + releases | — | ✅ | Verifier-approved quarter summaries, call quotes, derived narrative changes, management guidance revisions, and reported results versus prior company guidance |
| **Live Quotes** | Licensed market feed | — | ✅ | Real-time US equity quotes |
| **Options Chains** | Licensed options feed | — | ✅ | Chains by expiration and strike with bid/ask, volume, open interest, implied volatility, and delta/gamma/theta/vega |
| **Company KPIs** | Filings + earnings releases | — | ✅ | Operating metrics a company reports but XBRL never standardised, plus GAAP-to-non-GAAP bridges |
| **Guidance** | Earnings releases + calls | — | ✅ | Management's forward guidance, its ranges, and how it moved between quarters |
| **Buybacks & ATM** | 8-K / 10-Q | — | ✅ | Authorised repurchase programs and at-the-market equity programs, with amounts and dates |
| **IPO Feed** | SEC S-1 / F-1 | — | ✅ | New registrations with offer terms, underwriters, risk factors, and pre-IPO financials |
| **Investor Relations** | Company IR sites | — | ✅ | Upcoming events, conference appearances, and IR newsroom releases |
| **Executives** | SEC 8-K / DEF 14A | — | ✅ | Appointments and departures, plus Summary Compensation Table figures |
| **Valuation Multiples** | Derived | — | ✅ | Current and historical P/E, P/S, P/B, EV/EBIT, EV/EBITDA and more, with the periods each was computed from |
| **Screener** | Derived | — | ✅ | Filter the whole US market on valuation, margins, growth, ownership, short data, and dividends |
| **Smart Money** | Derived | — | ✅ | Insider sentiment scores, super-investor portfolios, and market-wide congressional activity |
| **Risk Flags** | SEC filings | — | ✅ | Going-concern language and customer-concentration disclosures |
| **Market Context** | Derived | — | ✅ | Correlated stocks, plus market calendar and open/closed status |
| **Index Composition** | SEC N-PORT + fund holdings files | — | ✅ | Constituents and membership-change history for the S&P 500/400/600, Nasdaq-100, Russell 1000/2000, and the Dow, plus rule-based addition and deletion forecasts |
| **Portfolios** | Your own account | — | ✅ | Track holdings and watchlists across stocks and options, with cost basis and realized/unrealized P&L. The only tools on the server that write |

## Why Some Data Is Cloud-Only

Nothing above is held back from this repo. The cloud-only rows are data whose *production* needs infrastructure you would not stand up at home:

- **Transcripts and audio** — earnings calls are webcasts, not filings. Each one has to be discovered and recorded by a browser fleet, then put through GPU speech-to-text with speaker diarization, every call, every quarter.
- **Live quotes and options chains** — licensed real-time feeds, plus an always-on streaming service to fan them out. Neither the licence nor the uptime fits in a container on a laptop.
- **KPIs, guidance, buybacks, IPO terms, executives, risk flags** — pulled out of filings, proxies, and earnings releases by LLM extraction lanes with verification passes and a human review queue, which needs sustained inference capacity.
- **Investor-relations events and news** — collected from thousands of company IR sites, needing the same stealth browser fleet as the webcasts.
- **Multiples, screener, smart money, market context** — derived, and only meaningful over a fully backfilled corpus of the whole market rather than the tickers you happen to have scraped so far.
- **Index composition** — assembled from every tracking fund's N-PORT schedule plus each fund's own holdings file fetched daily, which needs the full NPORT corpus and a fetcher that keeps re-learning where those files moved to.
- **Portfolios** — not scraped data at all: it is your own, stored against your account. The self-hosted build has no accounts, so there is nothing to store it against.

Beyond the data, the cloud is already backfilled and kept current, and it is managed — no scrapers to babysit. This repo is free forever under AGPL-3.0; the cloud has a free tier at 100 requests/day with no card.

Start on [the free tier](https://equibles.com/mcp), and self-host whenever you'd rather own the stack.

## Quick Start

### Docker Compose (recommended)

The fastest way to get everything running. Requires Docker.

```bash
git clone https://github.com/daniel3303/Equibles.git
cd Equibles
cp .env.example .env
# Edit .env and set SEC_CONTACT_EMAIL (required by SEC EDGAR fair access policy)
docker compose up
```

This starts:

| Service | Port | Description |
|---------|------|-------------|
| **db** | 5432 | ParadeDB (PostgreSQL + pgvector + pg_search) |
| **web** | 8080 | Web portal for browsing data |
| **mcp** | 8081 | MCP server for AI assistants |
| **worker** | — | Scrapers (SEC, FINRA, Congress, FRED, Yahoo, CFTC, CBOE, USAspending, FDA) |

Data scraping starts automatically. SEC filings, holdings, insider trades, and congressional trades will begin populating within minutes.

## Configuration

All settings can be configured via a `.env` file in the project root (recommended for Docker) or environment variables.

**FINRA Short Data (free API key required):**

The FINRA scraper (short volume and short interest) requires a free API key. Without it, the scraper skips gracefully and all other scrapers run normally. Fails-to-deliver data comes from SEC and works without FINRA credentials.

To get a key:

1. Go to the [FINRA API Console](https://gateway.finra.org/app/api-console) and sign in (a Google account works)
2. Open the **API Credentials** menu and create a new **API Key**
3. Copy the **Client ID** and **Client Secret**
4. Set `Finra__ClientId` and `Finra__ClientSecret` in your `.env` file or environment variables

> The older `developer.finra.org` "Teams & Apps" flow has been retired — use the API Console above.

**FRED Economic Data (free API key required):**

The FRED scraper requires a free API key from the Federal Reserve Bank of St. Louis. Without it, the scraper skips gracefully and all other scrapers run normally.

To get a key:

1. Register at [fred.stlouisfed.org/docs/api/api_key.html](https://fred.stlouisfed.org/docs/api/api_key.html)
2. Copy the 32-character API key
3. Set `Fred__ApiKey` in your `.env` file or environment variables

**Ticker Filtering (optional):**

By default, all tickers are synced. To limit data syncing to specific stocks, set a single ticker list that applies to all scrapers:

```env
# .env — sync only these tickers (applies to all scrapers)
Worker__TickersToSync__0=AAPL
Worker__TickersToSync__1=MSFT
Worker__TickersToSync__2=GOOGL
```

When not set, all stocks are synced.

**Minimum Sync Date (optional):**

By default, all scrapers start from January 2020. Set a more recent date for faster initial sync, or go as far back as 2000-01-01 for more historical data:

```env
# .env — start syncing from 2024 instead of 2020
Worker__MinSyncDate=2024-01-01
```

**Embedding (opt-in):**

| Setting | Default | Description |
|---------|---------|-------------|
| `Embedding__Enabled` | `false` | Set to `true` to enable vector embedding generation |
| `Embedding__Provider` | `Ollama` | `Ollama` (`/api/embed`) or `OpenAI` (`/v1/embeddings` — vLLM, Text-Embeddings-Inference, or OpenAI) |
| `Embedding__BaseUrl` | — | Embedding endpoint (e.g., `http://localhost:11434`) |
| `Embedding__ModelName` | — | Model name (e.g., `qwen3-embedding:0.6b`) |
| `Embedding__ApiKey` | — | Bearer token, if the server requires one (e.g. vLLM `--api-key`) |
| `Embedding__BatchSize` | `10` | Texts per embedding batch |

**Update notifications (optional):**

| Setting | Default | Description |
|---------|---------|-------------|
| `CHECK_FOR_UPDATES` | `true` | When `true`, the web portal checks GitHub Releases and shows a banner when a newer version is available. Set to `false` to disable. |

**Authentication (optional):**

| Setting | Default | Description |
|---------|---------|-------------|
| `AUTH_USERNAME` | — | Web portal username (auth disabled if empty) |
| `AUTH_PASSWORD` | — | Web portal password (auth disabled if empty) |
| `MCP_API_KEY` | — | MCP server API key (auth disabled if empty) |

When set, the web portal requires login and the MCP server requires `Authorization: Bearer <key>` header. When unset, everything is open access (default).

## Updating

The web portal checks GitHub Releases on a schedule and shows a banner when a newer version is available (disable with `CHECK_FOR_UPDATES=false`). To update to the latest release:

**Docker Compose:**

```bash
git pull
docker compose up -d --build --remove-orphans
```

Include any optional Compose override files in the upgrade command so those services remain enabled.

**From source:**

```bash
git pull
dotnet build Equibles.sln
```

Database migrations are applied automatically on startup. Review the [changelog](CHANGELOG.md) for notable changes before upgrading.

## Web Portal

The web portal at `http://localhost:8080` provides a browser-based interface for exploring data:

- **Stocks** — Browse and search all tracked companies, view price charts with technical indicators (SMA, EMA, RSI, MACD, Bollinger Bands), golden/death-cross and price-streak badges, performance versus SPY, plus per-stock tabs for institutional holdings, short data, SEC filings, financial statements, insider trading, proposed (Form 144) sales, fund holdings (NPORT), fund operations (N-CEN), exempt offerings (Form D), and congressional trades
- **Institutions** — Browse institutional holders (hedge funds, asset managers), view detailed profiles with portfolio breakdowns, industry allocation, quarterly activity, backtesting, and side-by-side comparisons. Filers are scored on risk-adjusted performance (alpha vs. a benchmark) and a Smart Money Index page aggregates the highest-scoring funds into a consensus signal. Includes a holdings screener with filters (filer count, value, float %, location, AUM/position-count, industry) and CSV export
- **Advisers** — Browse SEC-registered investment advisers (`/advisers`), ranked by assets under management, with per-firm profiles (regulatory AUM, main office, employee count, fee structure)
- **Insider Trading** — Dashboard showing the top insider buys, sells, and biggest transactions over the last 90 days
- **Short Activity** — Most-shorted leaderboard (`/most-shorted`, ranked by FINRA short interest) and largest daily short volume (`/short-volume`), each with a date selector, server-side sort, and pagination
- **Economy** — Browse FRED economic indicators grouped by category (interest rates, inflation, employment, GDP, etc.) with charts and statistics
- **Futures** — CFTC Commitments of Traders positioning data for 30+ futures contracts (commodities, indices, currencies) with commercial/non-commercial position charts
- **Market** — CBOE market indicators: VIX volatility index with OHLC charts, put/call ratios (equity, index, total, VIX, ETP)
- **Search** — Global search across stocks, institutions, insiders, and congress members with category filtering and date ranges
- **Status** — System health, worker status, data counts, and error log

## MCP Server

The MCP server exposes financial data tools for AI assistants (Claude, ChatGPT, etc.):

- **Institutional Holdings** — Top holders, ownership history, institution portfolios and summary, sector allocation, quarterly activity, most-held stocks, consensus holdings, fund overlap, market-wide 13F activity, institution search
- **Insider Trading** — Insider transactions, ownership summary, proposed (Form 144) sales, insider search
- **Congressional Trading** — Trades for a ticker, trades by one member, member search
- **SEC Documents** — Full-text search, semantic search, document browsing, keyword search within filings
- **Financial Statements** — XBRL fact time series per ticker, cross-ticker fact comparison, full income statement / balance sheet / cash flow per fiscal period
- **Fund & Adviser Filings** — Fund portfolio holdings (NPORT), registered-fund operations (N-CEN), exempt offerings (Form D), and SEC-registered investment-adviser lookup and search (Form ADV)
- **Short Data** — Daily short volume, market-wide largest daily short volume, bi-monthly short interest, and the latest short-interest snapshot across tickers
- **Economic Indicators** — FRED data lookup, latest macro snapshot, indicator search across categories
- **Stock Prices** — Daily OHLCV history with adjusted close, latest close across one or more tickers, and on-demand technical indicators (EMA, Stochastic Oscillator, Average True Range, On-Balance Volume, Bollinger Bands)
- **Futures Positioning** — COT positioning data, latest snapshot across all contracts, contract search
- **Market Indicators** — VIX historical data, put/call ratios by type (equity, index, total, VIX, ETP)
- **Government Contracts** — Federal contract awards won by a ticker (awarding agency, amount, period dates) and a ranking of the top public-company contractors by total federal dollars over a date range (USAspending.gov)
- **FDA Catalysts** — Scheduled FDA advisory-committee (AdComm) meetings over a date range — the regulatory catalyst dates that move biotech and pharma stocks (FDA.gov)

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

## Tools

This self-hosted build exposes 64 tools over MCP. The hosted server at `https://mcp.equibles.com/mcp` exposes 108 — the same 64 plus [44 more](#whats-included). Full catalog and client setup: [daniel3303/stock-market-mcp-server](https://github.com/daniel3303/stock-market-mcp-server).

**13F institutional holdings**

- GetFundCloneBacktest — backtest cloning a filer's 13F portfolio vs a benchmark
- GetTopHolders — top institutional holders of a stock (13F-HR)
- GetOwnershipHistory — institutional ownership trend across quarters
- GetInstitutionPortfolio — an institution's 13F portfolio
- SearchInstitutions — strict-first tokenized name/CIK search with filing date, 13F AUM, and position count
- GetTopBuyersSellers — biggest adds/reductions on a stock this quarter
- GetMarketWide13FActivity — market-wide 13F leaderboards
- GetMostHeldStocks — stocks by institutional breadth
- GetInstitutionSummary — filer summary (value, concentration, turnover)
- GetInstitutionSectorAllocation — allocation by sector
- GetInstitutionQuarterlyActivity — initiated/increased/reduced/exited vs prior quarter
- GetFundOverlap — portfolio overlap between two institutions
- GetConsensusHoldings — consensus portfolio of 2–25 institutions

**Insider trading**

- GetInsiderTransactions — insider transactions from Form 3/4/5
- GetInsiderOwnership — insider ownership ranked by shares
- GetProposedSales — proposed sales from Form 144
- SearchInsiders — strict-first whole-word filed-name search with verified public-name aliases

**SEC filings search**

- SearchDocuments — hybrid keyword+semantic search across all filings
- SearchCompanyDocuments — one company's filings by ticker
- SearchDocument — search within a single filing
- ListCompanyDocuments — browse a company's filings
- SearchDocumentKeyword — keyword search in one filing
- ReadDocumentLines — read a line range from a filing

**Funds, ETFs & advisers**

- SearchInvestmentAdvisers — strict-first tokenized adviser-name search (Form ADV)
- GetInvestmentAdviser — ADV profile by CRD
- GetFundOperations — fund ops from N-CEN with exact directory-identifier resolution
- GetFundHoldings — NPORT-P holdings with full reported versus stored counts; trust-series rows identify their tracked-stock subset
- GetFundsHoldingStock — funds holding a stock
- GetExemptOfferings — private placements (Form D)
- GetFailsToDeliver — SEC FTD data
- SearchFunds — strict-first tokenized fund/registrant/ticker search with verified share-class aliases
- GetFundProfile — fund profile + largest stored holdings with explicit coverage counts

**Fundamentals (XBRL)**

- GetFinancialStatement — income/balance/cash-flow statement anchored to one period end, with each line's exact span
- GetFinancialFact — one concept over time with exact spans and stale-alias coverage warnings
- CompareFinancialFact — a concept across companies
- GetRevenueBreakdown — revenue by segment/geography/product plus reported segment operating income and derived margin

**Economic data (FRED)**

- GetEconomicIndicator — a FRED series
- GetLatestEconomicData — latest values by category
- SearchEconomicIndicators — strict-first tokenized curated-series search with standard macro aliases
- GetEconomicCalendar — US macro release calendar

**Futures (CFTC COT)**

- GetCftcPositioning — COT positioning for a contract
- GetLatestCftcData — latest COT snapshot
- SearchCftcMarkets — strict-first tokenized search by name, code, or standard futures symbol

**Volatility (CBOE)**

- GetPutCallRatios — put/call ratios
- GetVixHistory — VIX daily OHLC

**FDA calendar**

- GetFdaCatalysts — scheduled FDA advisory-committee meetings

**Congressional trading**

- GetCongressionalTrades — trades for a ticker
- GetMemberTrades — a member's trades
- GetMemberNetWorth — member net worth history
- SearchCongressMembers — strict-first tokenized roster search with verified public-name aliases

**Short data (FINRA)**

- GetShortVolume — daily short volume
- GetShortInterest — bi-monthly short interest
- GetShortInterestSnapshot — market-wide snapshot
- GetLargestShortVolume — largest daily short volume
- GetShortSqueezeScores — composite squeeze scores
- GetOffExchangeVolume — dark-pool/OTC volume

**Stock prices**

- GetStockPrices — daily OHLCV plus the stored auxiliary adjusted close when it differs
- GetLatestPrices — latest close/change/volume
- GetStochasticOscillator — stochastic oscillator (%K/%D)
- GetAverageTrueRange — average true range (ATR)
- GetOnBalanceVolume — on-balance volume (OBV)
- GetBollingerBands — Bollinger Bands

**Government contracts (USAspending)**

- GetGovernmentContracts — federal contract awards for a company
- GetTopGovernmentContractors — largest public-company contractors

## Vector Embeddings (advanced, opt-in)

Vector embeddings enable semantic search over SEC filings (e.g., "find revenue growth discussion in Apple's 10-K"). This requires downloading the Ollama runtime (~2GB) and the Qwen3-Embedding-0.6B model (~640MB).

```bash
docker compose -f docker-compose.yml -f docker-compose.embedding.yml up
```

This adds Ollama and configures the existing web, MCP, and worker services to use it:

| Service | Port | Description |
|---------|------|-------------|
| **embedding** | 11434 | Ollama server with Qwen3-Embedding-0.6B model |
| **embedding-pull** | — | One-shot model download before the worker starts |
| **worker** | — | The existing worker, with embedding generation enabled |

Without the embedding override, BM25 full-text search via ParadeDB still works out of the box — vector search is purely additive.

**Bundled Ollama is the default** because it needs no GPU and runs anywhere. For bulk embedding at scale, point `Embedding__Provider=OpenAI` at a batched server such as [vLLM](https://docs.vllm.ai) or [Text-Embeddings-Inference](https://github.com/huggingface/text-embeddings-inference) — they continuously batch on the GPU and are far faster than Ollama for large corpora. See [docs/guide/how-to-use-external-embedding-endpoint.md](docs/guide/how-to-use-external-embedding-endpoint.md).

## Screenshots

<table>
  <tr>
    <td width="50%"><strong>Stock Detail</strong><br><img alt="Stock detail page showing price charts, moving averages, and technical indicators for AAPL" src="docs/screenshots/stock-detail.png"></td>
    <td width="50%"><strong>Stocks</strong><br><img alt="Stocks page with search and ticker listing" src="docs/screenshots/stocks-list.png"></td>
  </tr>
  <tr>
    <td width="50%"><strong>Economic Data</strong><br><img alt="Economic indicators grouped by category" src="docs/screenshots/economy.png"></td>
    <td width="50%"><strong>Economic Indicator Detail</strong><br><img alt="Federal Funds Rate chart and observations" src="docs/screenshots/economy-detail.png"></td>
  </tr>
</table>

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, project architecture, and how to extend the platform. Contributors must sign the [Contributor License Agreement](CLA.md) — this is handled automatically by a bot when you open a pull request.

## License

[AGPL-3.0](LICENSE)

## Author

Daniel Oliveira

[![Website](https://img.shields.io/badge/Website-FF6B6B?style=for-the-badge&logo=safari&logoColor=white)](https://danielapoliveira.com/)
[![X](https://img.shields.io/badge/X-000000?style=for-the-badge&logo=x&logoColor=white)](https://x.com/daniel_not_nerd)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-0077B5?style=for-the-badge&logo=linkedin&logoColor=white)](https://www.linkedin.com/in/daniel-ap-oliveira/)
