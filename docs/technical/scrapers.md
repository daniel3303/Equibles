# Scrapers and Integrations

How the `*.HostedService` workers ingest data, how the `Equibles.Integrations.*` HTTP clients talk to upstream APIs, and how the deduplication ledgers keep re-runs idempotent.

## Two-layer shape

- **HostedService** — orchestration. Inherits `BaseScraperWorker`, decides what to fetch and when, persists rows through repositories / managers.
- **Integrations** — outbound HTTP. Plain client classes (`SecEdgarClient`, `FredClient`, `YahooFinanceClient`, …) that know nothing about EF, repositories, or workers — pure protocol code.

The split keeps the database concerns and the protocol concerns testable in isolation. An integration client returns DTOs; the HostedService maps DTOs onto entities.

## Worker base — [`BaseScraperWorker`](../../src/Equibles.Worker/BaseScraperWorker.cs)

Abstract `BackgroundService` that every scraper extends. Subclass surface:

| Member | Role |
|---|---|
| `WorkerName` | Log prefix + error-source label. |
| `SleepInterval` | Time between successful cycles (typically 24h). |
| `ErrorSource` | The `ErrorSource` smart-enum used when reporting failures. |
| `DoWork(stoppingToken)` | The actual work for one cycle. |
| `ValidateConfiguration()` | Optional gate; return `false` to short-circuit the loop on missing config. |

Built-in behavior:

- One try/catch per cycle. Unhandled exceptions go through `ErrorReporter.Report(ErrorSource, "<Worker>.DoWork", ...)` and the loop sleeps to the next cycle instead of crashing the host.
- `OperationCanceledException` during shutdown logs and exits cleanly — never reported as an error.
- `RequestRetrySoon()` — sets a flag mid-cycle that swaps the next wait from `SleepInterval` to `NotReadyRetryInterval` (default 2 minutes). Used when a dependency isn't ready yet (e.g. cold-start race where `CommonStock` hasn't been seeded). Flag resets at the start of every cycle.

Subclass-specific extras (worth knowing because they show up in multiple workers):

- `WaitForNextCycle(interval, stoppingToken)` — override hook to interrupt the sleep on an external signal. [`HoldingsScraperWorker`](../../src/Equibles.Holdings.HostedService/HoldingsScraperWorker.cs) uses it to wake immediately when `HoldingsRescanSignal` fires after a `StockCusipChanged` event.
- Per-attempt retry delays exposed as `protected virtual` properties (e.g. `RetryDelays = [30s, 2m, 10m]`) so tests can collapse them without changing production.

## Integration clients — [`Equibles.Integrations.*`](../../src/Equibles.Integrations.Common)

Each upstream source has its own integration project:

| Project | Upstream |
|---|---|
| `Equibles.Integrations.Sec` | SEC EDGAR (filings, FTD, submissions JSON, XBRL companyfacts) |
| `Equibles.Integrations.Fred` | Federal Reserve Bank of St. Louis FRED |
| `Equibles.Integrations.Yahoo` | Yahoo Finance (`query1.finance.yahoo.com`) |
| `Equibles.Integrations.Finra` | FINRA API |
| `Equibles.Integrations.Cftc` | CFTC Commitments of Traders |
| `Equibles.Integrations.Cboe` | CBOE indicators |
| `Equibles.Integrations.Common` | Shared infrastructure — currently `RateLimiter` only |

Every client follows the same shape: one interface + one implementation registered via `[Service(ServiceLifetime.Scoped, typeof(IXxxClient))]` (the AutoWire attribute from `Equibles.Core.AutoWiring`). The interface lives in `Contracts/`, the DTOs in `Models/`, and the wire/transport details in the client class itself.

### Rate limiting — [`RateLimiter`](../../src/Equibles.Integrations.Common/RateLimiter)

- `IRateLimiter` with `WaitAsync()` + `PauseFor(TimeSpan)`. Each client owns a `static readonly IRateLimiter` configured for its upstream's published (or reverse-engineered) limit — e.g. Yahoo uses `40 requests / minute`.
- `WaitAsync()` blocks until the sliding-window counter allows another request; calls itself recursively after the delay so a long pause never silently lets a request through.
- `PauseFor(TimeSpan)` extends the no-go window — used when the upstream returns `429` to widen the cooldown beyond what the steady-state counter would impose. The pause is monotonic (a later, shorter pause never shortens an already-set later deadline).

### Retry pattern

Two retry shapes appear across the clients; pick by upstream behaviour:

- **Bounded exponential backoff** (FRED, Yahoo, CBOE) — `for attempt in 0..MaxRetries`, sleep `2^attempt` seconds on `429` / `5xx` / network failure, give up after the last attempt with `HttpRequestException("Max retries exceeded …")`.
- **Auth-refresh + retry** (FINRA, Yahoo session cookies) — on `401` / `403`, invalidate the cached token / cookie, refresh, retry once. Distinct from the rate-limit retry because the failure mode is "session expired" rather than "too fast".

Anything mid-cycle that the upstream guarantees is transient (e.g. a partial JSON response) is converted into a structured error and reported via `ErrorReporter`, not retried into oblivion.

## Bookkeeping

Workers that fetch from a paginated / batched feed maintain a dedup ledger so re-running the same cycle is idempotent.

### Holdings — `ProcessedDataSet` + `ProcessedFiling`

[`ProcessedDataSet`](../../src/Equibles.Holdings.Data/Models/ProcessedDataSet.cs):

- Keyed by SEC quarterly bulk-data-set file name (`form13fhr_2024q3_01.zip`).
- Marks a file as fully ingested so the next `HoldingsScraperWorker` cycle skips it.
- Stores a `SubmissionCount` for observability.
- Contains a sentinel row `BackfillGuardFileName = "__backfill-guard__"` — a name that never matches a real file. Its purpose is to keep the table non-empty after `StockCusipChangedConsumer` clears real rows for a backfill, so `BackfillProcessedDataSets` doesn't re-seed history as "processed" before the backfill actually runs.

[`ProcessedFiling`](../../src/Equibles.Holdings.Data/Models/ProcessedFiling.cs):

- Keyed by accession number.
- Recorded by [`Holdings13FRealtimeWorker`](../../src/Equibles.Holdings.HostedService/Holdings13FRealtimeWorker.cs) for every individual 13F-HR submission already handed to the import pipeline.
- An amendment carries a new accession number, so it is still processed; a previously-handled original is never re-processed. Without this ledger, re-sweeping the daily index after an amendment's delete-by-period would upsert stale originals back over the amendment.
- Filings that produced zero tracked holdings are recorded too — otherwise the same empty filing would be re-downloaded every cycle.

### SEC — `Document` rows act as the ledger

Per-document `Equibles.Sec.Data.Models.Document` rows carry the accession + the processing status. `DocumentScraper` writes the row on first sight; `DocumentProcessorWorker` flips it to processed after the per-document-type processor (`InsiderTradingFilingProcessor`, etc.) returns successfully. A failure leaves the row in its current state so the next cycle retries.

### FTD / FINRA / FRED / Yahoo / CFTC / CBOE — "latest date" cursor

These sources publish a continuous time series rather than discrete filings. Each scraper resolves the start date via `SyncDateResolver.Resolve(latestInDb, workerOptions)`:

- If the database already has data → start from `latestInDb + 1 day`.
- Otherwise → `WorkerOptions.MinSyncDate` if set, else `2020-01-01`.

The cursor pattern means re-running is cheap (a single query for `max(date)` per cycle); duplicate ingestion is prevented by the per-source unique index (`[Index(nameof(CommonStockId), nameof(Date), IsUnique = true)]` etc.).

## Cold-start patterns

- Empty `CommonStock` table → most scrapers `RequestRetrySoon()` and wait `NotReadyRetryInterval` (2 min) instead of the full `SleepInterval` (24h). Pattern handles the first ~30 min after a fresh deploy where `CompanySyncService` is still populating the universe.
- Realtime workers depend on the bulk-data backfill having seeded history. Until then the realtime worker still runs but produces no rows; once `BackfillProcessedDataSets` has run, the realtime path takes over.

## Error reporting

- Every worker has an `ErrorReporter` injected and a fixed `ErrorSource` (the smart-enum value).
- Unexpected exceptions surface as rows in the `Error` table via `ErrorReporter.Report(source, location, message, stackTrace)`.
- The Web portal's Status page reads from the same table — a failing scraper shows up there within seconds of `ErrorReporter.Report` returning, even if the worker keeps running.
- Reporting itself never throws into the worker — `Report` swallows its own failures and logs them so a sick `Error` table doesn't take down a healthy scraper.

## Rescan signals

In-process pub/sub between modules:

- [`HoldingsRescanSignal`](../../src/Equibles.Holdings.HostedService/HoldingsRescanSignal.cs) — singleton with an internal `TaskCompletionSource` queue.
- `StockCusipChangedConsumer` (MassTransit) calls `HoldingsRescanSignal.Signal()` after invalidating processed data.
- `HoldingsScraperWorker.WaitForNextCycle` races the signal against its 24h timer and wakes on whichever fires first.
- Pattern is reusable. Add a singleton with the same async-signal shape (`WaitAsync` / `Signal`) when one module's events should wake another module's worker.

## Adding a new scraper

1. Add a new project `src/Equibles.<Module>.HostedService` referencing `Equibles.Worker`, the module's `.Data` and `.Repositories` projects, and the matching `Equibles.Integrations.<Source>`.
2. Worker class inherits `BaseScraperWorker`. Set `WorkerName`, `SleepInterval`, `ErrorSource`; implement `DoWork`.
3. Register the worker with `services.AddHostedService<MyScraperWorker>()` from a `ServiceCollectionExtensions.Add<Module>Worker(this IServiceCollection)` method.
4. Add `builder.Services.Add<Module>Worker()` to [`Equibles.Worker.Host/Program.cs`](../../src/Equibles.Worker.Host/Program.cs).
5. If the scraper has tunables, define `<Module>ScraperOptions`, bind it in the host with `services.Configure<...>(builder.Configuration.GetSection("<Module>Scraper"))`, and inject `IOptions<...>` into the worker.
6. If the source is paginated → write a `Processed<X>` ledger table. If it's a continuous time series → use `SyncDateResolver.Resolve` against `max(date)`.
