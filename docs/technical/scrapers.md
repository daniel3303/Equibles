# Scrapers and Integrations

## European ESEF report history

- ESEF capture matches source LEIs to verified issuers without a SEC CIK.
- Future reporting periods in index metadata are rejected before selection; completed reports must not be masked by an erroneous future date.
- A reporting period later than the index's stated receipt date remains invalid even after that period becomes historical.
- Future receipt dates are excluded before selection and cannot become stored filing dates.
- Select one report per source-stated reporting period, newest first, using the existing deterministic country and validation ordering.
- Capture at most one missing period per issuer per cycle; issuers missing their latest report take priority over historical backfill.
- Stored reports and terminal size refusals do not block earlier periods or consume download budget.
- An HTML size refusal makes the index's exact `json_url` eligible on the next cycle; the refused HTML is not downloaded again.
- Both representations retain the 50 MiB ceiling and shared host pacing; a JSON size refusal closes that filing until its representation or ceiling changes.
- Preserve the HTML URL and ceiling independently when recording a JSON refusal; a changed JSON URL must not redownload unchanged oversized HTML.
- Require conflict-free facts at the indexed period and reject later supported issuer periods; a comparative alone cannot validate a misaddressed report.
- JSON capture retains the original bytes and source URL, with no invented retrieval text; unsupported or mismatched issuer/period evidence stays retryable.
- Structured recovery publishes only unqualified standard IFRS facts with an exact ISO 17442 issuer identity; dimensional, custom, unsupported numeric and timezone-bearing period shapes remain unavailable.
- xBRL-JSON values are already scaled; `decimals` describes precision, and midnight instant/end timestamps map to the preceding inclusive calendar date.
- Recovery is recurring reconciliation, not a one-time script; no extraction-version bump replays already captured HTML documents.
- An ESEF index entry does not establish annual reporting: tagged interim reports use the same format.
- Validate inline XML namespaces in scope before annual classification and fact extraction; ambiguous rebindings, spoofed instance namespaces and aliases unsupported by the shared parser keep the general type and refuse financial-fact publication. A default inline-XBRL binding is accepted for text blocks; a numeric fact without the literal `ix` prefix still refuses the report.
- Unit measures resolve from ISO 4217, `xbrli:pure`/`xbrli:shares`, or any other declared namespace (the XBRL Unit Type Registry or an issuer extension such as an employee count); a bare name that reads as a currency code, `pure` or `shares`, or one bare name declared in two namespaces, still refuses the report.
- Retain refused originals for diagnosis; the extraction guard prevents new unsupported facts but does not remove previously stored rows or replay the completed corpus.
- Classify a captured report as annual only with matching unqualified, conflict-free IFRS annual-duration and instant facts at its indexed end, and no later supported issuer facts.
- Keep other captured reports as the general `EsefReport` type; preserve their envelope, financial extraction, normalization replay and history deduplication.
- Initialize a missing fiscal calendar only when the currently captured report proves the latest indexed annual period; an unread latest report or an older backfill cannot supply that evidence.
- Existing fiscal metadata remains unchanged; correcting historical report types, fiscal calendars and fact labels requires a separate source-backed repair.
- Historical ingestion is recurring reconciliation: subsequent cycles discover missing periods, and a fully captured issuer makes no report requests.
- Capturing an XBRL envelope queues existing financial-fact extraction; capture counts alone do not prove published financial coverage.

How the `*.HostedService` workers ingest data, how the `Equibles.Integrations.*` HTTP clients talk to upstream APIs, and how the deduplication ledgers keep re-runs idempotent.

## Retained SEC originals

- Live capture and the original-document backfill share `AsFiledHtmlDocumentTypes.Supported`: 8-K, 10-K, 10-Q, 20-F, 40-F, 6-K and their amendments.
- Periodic reports retain their primary HTML and referenced images even without an HTML exhibit; 8-K stitching still requires a displayable exhibit.
- The backfill selects supported reports below the current builder version, with a known CIK, a stored or recoverable accession, and fewer than five failed attempts.
- Reporting dates do not limit the backfill; completed captures stay complete when a new form is added to the supported set.
- Original capture preserves the separate XBRL envelope and derived financial facts; image requests retain the shared SEC pacing and existing count/byte limits.

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
- `RequestRetrySoon()` — sets a flag mid-cycle that swaps the next wait from `SleepInterval` to `NotReadyRetryInterval` (default 2 minutes).
- Used when a dependency isn't ready yet (e.g. cold-start race where `CommonStock` hasn't been seeded).
- The flag resets at the start of every cycle.

Subclass-specific extras (worth knowing because they show up in multiple workers):

- `WaitForNextCycle(interval, stoppingToken)` — override hook to interrupt the sleep on an external signal.
- [`HoldingsScraperWorker`](../../src/Equibles.Holdings.HostedService/HoldingsScraperWorker.cs) uses `WaitForNextCycle` to wake immediately when `HoldingsRescanSignal` fires after a `StockCusipChanged` event.
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
| `Equibles.Integrations.GovernmentContracts` | USAspending.gov federal contract awards |
| `Equibles.Integrations.Wikidata` | Wikidata SPARQL endpoint (company metadata discovery) |
| `Equibles.Integrations.Common` | Shared infrastructure — `RateLimiter` plus the `RetryBackoff` exponential-backoff formula and the `HttpRetry.Send` 429/5xx retry helper |

Every client follows the same shape: one interface + one implementation registered via `[Service(ServiceLifetime.Scoped, typeof(IXxxClient))]` (the AutoWire attribute from `Equibles.Core.AutoWiring`). The interface lives in `Contracts/`, the DTOs in `Models/`, and the wire/transport details in the client class itself.

### Rate limiting — [`RateLimiter`](../../src/Equibles.Integrations.Common/RateLimiter)

- `IRateLimiter` exposes `WaitAsync()` + `PauseFor(TimeSpan)`.
- Each client owns a `static readonly IRateLimiter` configured for its upstream's published (or reverse-engineered) limit — e.g. Yahoo uses `40 requests / minute`.
- `WaitAsync()` blocks until the sliding-window counter allows another request; calls itself recursively after the delay so a long pause never silently lets a request through.
- `PauseFor(TimeSpan)` extends the no-go window — used when the upstream returns `429` to widen the cooldown beyond what the steady-state counter would impose.
- The pause is monotonic: a later, shorter pause never shortens an already-set later deadline.

### Retry pattern

Two retry shapes appear across the clients; pick by upstream behaviour:

- **Bounded exponential backoff** (FRED, Yahoo, CBOE) — `for attempt in 0..MaxRetries`, sleep `2^attempt` seconds on `429` / `5xx` / network failure.
- After the last attempt, give up with `HttpRequestException("Max retries exceeded …")`.
- **Auth-refresh + retry** (FINRA, Yahoo session cookies) — on `401` / `403`, invalidate the cached token / cookie, refresh, retry once.
- Distinct from the rate-limit retry because the failure mode is "session expired" rather than "too fast".

Anything mid-cycle that the upstream guarantees is transient (e.g. a partial JSON response) is converted into a structured error and reported via `ErrorReporter`, not retried into oblivion.

## Bookkeeping

Workers that fetch from a paginated / batched feed maintain a dedup ledger so re-running the same cycle is idempotent.

### Holdings — `ProcessedDataSet` + `ProcessedFiling`

[`ProcessedDataSet`](../../src/Equibles.Holdings.Data/Models/ProcessedDataSet.cs):

- Keyed by SEC quarterly bulk-data-set file name (`form13fhr_2024q3_01.zip`).
- Marks a file as fully ingested so the next `HoldingsScraperWorker` cycle skips it.
- Stores a `SubmissionCount` for observability.
- Contains a sentinel row `BackfillGuardFileName = "__backfill-guard__"` — a name that never matches a real file.
- The sentinel keeps the table non-empty after `StockCusipChangedConsumer` clears real rows for a backfill, so `BackfillProcessedDataSets` doesn't re-seed history as "processed" before the backfill actually runs.

[`ProcessedFiling`](../../src/Equibles.Holdings.Data/Models/ProcessedFiling.cs):

- Keyed by accession number.
- Recorded by [`Holdings13FRealtimeWorker`](../../src/Equibles.Holdings.HostedService/Holdings13FRealtimeWorker.cs) for every individual 13F-HR submission already handed to the import pipeline.
- An amendment carries a new accession number, so it is still processed; a previously-handled original is never re-processed.
- Without this ledger, re-sweeping the daily index after an amendment's delete-by-period would upsert stale originals back over the amendment.
- Filings that produced zero tracked holdings are recorded too — otherwise the same empty filing would be re-downloaded every cycle.

### SEC — `Document` rows act as the ledger

Per-document `Equibles.Sec.Data.Models.Document` rows carry the accession + the processing status. `DocumentScraper` writes the row on first sight; `DocumentProcessorWorker` flips it to processed after the per-document-type processor (`InsiderTradingFilingProcessor`, etc.) returns successfully. A failure leaves the row in its current state so the next cycle retries.

### SEC Financial Facts — per-stock filing watermark + per-document version stamp

The two SEC Financial Facts workers each keep their own ledger.

- [`FinancialFactsScraperWorker`](../../src/Equibles.Sec.FinancialFacts.HostedService/FinancialFactsScraperWorker.cs) walks every CIK-bearing company and pulls its SEC Company Facts. [`FinancialFactsImportService`](../../src/Equibles.Sec.FinancialFacts.HostedService/Services/FinancialFactsImportService.cs) records a per-stock [`FinancialFactsSyncStatus`](../../src/Equibles.Sec.FinancialFacts.Data/Models/FinancialFactsSyncStatus.cs) row (keyed by `CommonStockId`) holding `LastFiledDateSeen` and `LastCheckedAt`.
- When the API's max `Filed` date is no newer than `LastFiledDateSeen`, the import skips the expensive full-history re-upsert and only refreshes the share count — so a re-run costs one Company Facts request per company, not a full reparse.
- `LastCheckedAt` also gates the walk itself: each cycle only visits companies never checked or checked before the `RecheckIntervalHours` window (default 20h; never-checked first, then stalest first). Because the sweep restarts on every host restart and each visit downloads the full Company Facts JSON before the watermark can say "nothing new", the window is what makes a restart resume the aborted sweep instead of re-downloading the whole universe.
- [`XbrlFactsExtractionWorker`](../../src/Equibles.Sec.FinancialFacts.HostedService/XbrlFactsExtractionWorker.cs) sweeps documents whose captured XBRL envelope has not been processed at the current `XbrlFactExtractionService.CurrentVersion`, stamped per document via `Document.XbrlFactsVersion` — see [Modules → SEC Financial Facts](modules.md) for the dimensional-facts detail.
- The SEC XBRL capture keeps a filing's whole inline document set, every HTML submission document (`.htm`, `.html`, `.xhtml`, or the named primary) carrying inline markers joined primary first, so a 40-F or 6-K tagging its cover in the primary and its statements in an exhibit is parsed whole.
  - A filing with one inline document captures it unchanged.
  - Each joined document has its empty XHTML elements expanded first, because the joined roots are no longer one XML document.
  - A set over `SecDocumentEnvelopeParser.MaxInlineDocumentSetBytes` (the fact parser's 50 MB ceiling) keeps only its primary.
- [`XbrlDocumentSetRecaptureWorker`](../../src/Equibles.Sec.HostedService/XbrlDocumentSetRecaptureWorker.cs) is a finite repair: periodic filings captured before 2026-10-15 whose facts carry no currency unit are re-read from EDGAR once, original 10-K, 20-F and 40-F reports first and then newest first; a capture that grew replaces the stored envelope and resets `XbrlFactsVersion`, and every finished re-read stamps `XbrlCaptureAttempts` at its ceiling, while a failed one counts one attempt. Retire it once `XbrlDocumentSetRecaptureService.Pending()` is empty in production.
- Foreign annual and interim reports also fill missing standard consolidated facts from captured XBRL. Recovery requires an unqualified matching SEC CIK context and the official financial taxonomy namespace; duplicate context IDs and conflicting values are refused. Inserts never update an existing Company Facts natural key. Version 6 replays foreign reports only; unchanged domestic version-5 captures do not repeat their parsing work.
- SEC sometimes never processes a filing into Company Facts (CHEF's Q2 FY2026 10-Q, accession `0001517175-26-000028`, carried no Company Facts row months after filing). A 10-Q/10-K whose accession has no consolidated standard Company Facts row fills its consolidated facts from captured XBRL the same way, tagged `FinancialFact.Source = FilingXbrl`.
  - Only periods the fiscal calendar resolves are filled; the calendar-quarter fallback is never used for them. The context's CIK may be the issuer's primary or any secondary CIK (foreign fills too).
  - Steady state: extraction usually runs before Company Facts picks a new filing up, so most fresh 10-Q/10-Ks are filled first and taken over within a day; only a filing SEC never processes keeps its fills.
  - The next Company Facts import that emits the accession re-stamps the rows it states as `CompanyFacts` and deletes the fills it does not state, even when its watermark would otherwise skip the cycle. Extension, dimensional and foreign-form rows are untouched.
  - If Company Facts lands while a document is being filled, the extractor withdraws its fills itself; the partial index `IX_FinancialFact_FilingXbrlFill` keeps these probes cheap.
  - Fills bypass `FinancialFactImportQualityFilter` until Company Facts takes over.
  - Version 9 is a targeted replay: documents at version 8 rerun only when they are 10-Q/10-K filings filed since 2026-01-01 or newer than their issuer's `LastFiledDateSeen`, which includes every 10-Q/10-K of an issuer Company Facts never covered (`XbrlFactExtractionService.TargetedReplayFloor`). Foreign documents are not replayed, so the secondary-CIK fill reaches them only when they rerun for another reason. The next full-corpus bump raises the floor to the new version and retires the replay.
  - `Source` is reliable only for rows written after the column was added: older rows default to `CompanyFacts`, which the gate and the cleanup never misread because both consider only consolidated, non-extension rows on a 10-Q/10-K accession.

### FTD / FINRA / FRED / Yahoo / CFTC / CBOE — "latest date" cursor

These sources publish a continuous time series rather than discrete filings. Each scraper resolves the start date via `SyncDateResolver.Resolve(latestInDb, workerOptions)`:

- If the database already has data → start from `latestInDb + 1 day`.
- Otherwise → `WorkerOptions.MinSyncDate` if set, else `2020-01-01`.

The cursor pattern means re-running is cheap (a single query for `max(date)` per cycle); duplicate ingestion is prevented by the per-source unique index (`[Index(nameof(CommonStockId), nameof(Date), IsUnique = true)]` etc.).

- Catalog prices rotate across venue identities inside both the recent-price and historical queues; a larger market cannot fill each bounded request batch ahead of smaller markets.
- Each venue retains oldest-attempt ordering across restarts, with four recent-price candidates per historical candidate and unchanged retry intervals.

### FDA catalysts — watermark-less re-read + upsert

[`FdaCatalystScraperWorker`](../../src/Equibles.FdaCatalysts.HostedService/FdaCatalystScraperWorker.cs) reconciles the forward-looking FDA advisory-committee calendar, which carries no historical watermark — every cycle re-reads the whole calendar rather than resuming from a `max(date)` cursor.

- [`FdaAdvisoryCommitteeCalendarImportService`](../../src/Equibles.FdaCatalysts.HostedService/Services/FdaAdvisoryCommitteeCalendarImportService.cs) parses the calendar, then upserts each meeting by its stable per-meeting slug (`FdaCatalyst.SourceReference`): an existing row refreshes its mutable fields, a new meeting inserts.
- The calendar is mutable — scheduled dates and venues shift before a meeting happens — so a `max(date)` cursor would skip edits to already-stored meetings. Re-reading every cycle keeps stored rows in sync.

### Government contracts — checkpointed windowed scan with parent fallback

[`GovernmentContractsImportService`](../../src/Equibles.GovernmentContracts.HostedService/Services/GovernmentContractsImportService.cs) walks USAspending in `WindowDays`-sized action-date windows (default 1 day), because the API requires a bounded range per request.

- A persisted checkpoint (`GovernmentContractsScanState.LastCompletedWindowEnd`) records the contiguous fully-scanned frontier. It advances only while every window this cycle has succeeded; the first failed window pulls it back behind that window.
- Each cycle resumes the day after the frontier, but never later than a trailing `RescanLookbackDays` window (default 7), so awards USAspending publishes late are still picked up. Without a checkpoint the start falls back to `SyncDateResolver` over `max(ActionDate)`. Every start is clamped to the 2007-10-01 epoch.
- Re-scanned awards de-duplicate against `LoadExistingKeys` by `AwardUniqueKey`, so only new awards insert.
- A 422 rejects only that window: it is reported and stepped over. Any other HTTP failure, or five consecutive window failures, aborts the cycle for the worker to back off.
- Unmatched awards are dropped, not stored. When the stored `MatchingVersion` is older than `RecipientMatchingVersion`, the next cycle pulls the checkpoint back to the epoch once so improved matching reaches history. Crossing version 3 also purges cached parentless answers so they are re-fetched.
- A recipient whose name matches no company gets one retry through its SAM parent from the recipient profile. The current parent and the `parents[]` history are grouped by registrant (DUNS, then UEI, then hash), and only a single registrant resolves; every name it has carried is tried by exact match.
- Entries naming the recipient itself (its own DUNS or UEI) are dropped when the current parent is a different registrant, since SAM records such eras during the owner's tenure. A subsidiary's pre-acquisition awards may therefore link to its current owner, as in USAspending's own roll-up.
- Parent answers cache in `GovernmentContractRecipientParent`, including "no usable parent". Readings stay fresh for 180 days; rows recording a failed profile fetch retry after 7 days. More than three failed profile fetches in one window fail the window instead of caching.

### Congress — fixed-window re-scan across both chambers

The two congressional scrapers re-read a fixed recent window every cycle instead of advancing a `max(date)` watermark, because members file and amend disclosures well after the reporting period — a monotonic cursor would step past a late filing and never pick it up.

- [`CongressionalTradeSyncService`](../../src/Equibles.Congress.HostedService/Services/CongressionalTradeSyncService.cs) re-reads trades from `MinSyncDate` (default: January 1 of the current year) to today from both the `SenateDisclosureClient` and `HouseDisclosureClient`, then matches each transaction to a tracked stock.
- One older chamber/year partition is replayed per cycle, newest missing year first down to the STOCK Act start in 2012. `CongressionalTradeImportPartition` is written only after the source index and every recordable filing complete, and stores the parser version so a later parser can reopen every year. Unavailable or malformed archives retry instead of becoming false empty years.
- `CongressionalFilingRecord.ParserVersion` replays filings after parser changes. The trade parser retains the filed instrument type and account/subholding, and treats a lone standard range floor as that disclosure bracket rather than as a zero-based band.
- [`CongressionalAnnualDisclosureSyncService`](../../src/Equibles.Congress.HostedService/Services/CongressionalAnnualDisclosureSyncService.cs) re-reads annual financial disclosures across a span of coverage years (House per year, Senate by submitted date) and upserts each report by its stable key, so a late amendment refreshes the stored row rather than inserting a duplicate.

## Cold-start patterns

- Empty `CommonStock` table → most scrapers `RequestRetrySoon()` and wait `NotReadyRetryInterval` (2 min) instead of the full `SleepInterval` (24h).
- The pattern handles the first ~30 min after a fresh deploy while `CompanySyncService` is still populating the universe.
- Realtime workers depend on the bulk-data backfill having seeded history; until then they still run but produce no rows.
- Once `BackfillProcessedDataSets` has run, the realtime path takes over.

## Error reporting

- Every worker has an `ErrorReporter` injected and a fixed `ErrorSource` (the smart-enum value).
- Unexpected exceptions surface as rows in the `Error` table via `ErrorReporter.Report(source, location, message, stackTrace)`.
- The Web portal's Status page reads from the same table — a failing scraper shows up there within seconds of `ErrorReporter.Report` returning, even if the worker keeps running.
- Reporting itself never throws into the worker — `Report` swallows its own failures and logs them so a sick `Error` table doesn't take down a healthy scraper.

## Rescan signals

In-process pub/sub between modules:

- [`HoldingsRescanSignal`](../../src/Equibles.Holdings.HostedService/HoldingsRescanSignal.cs) — singleton wrapping a `SemaphoreSlim(0, 1)`; repeated requests coalesce to a single pending rescan.
- `StockCusipChangedConsumer` (MassTransit) calls `HoldingsRescanSignal.RequestRescan()` after invalidating processed data.
- `HoldingsScraperWorker.WaitForNextCycle` races the signal against its 24h timer and wakes on whichever fires first.
- Pattern is reusable. Add a singleton with the same async-signal shape (`WaitAsync` / `Signal`) when one module's events should wake another module's worker.

## Adding a new scraper

1. Add a new project `src/Equibles.<Module>.HostedService` referencing `Equibles.Worker`, the module's `.Data` and `.Repositories` projects, and the matching `Equibles.Integrations.<Source>`.
2. Worker class inherits `BaseScraperWorker`. Set `WorkerName`, `SleepInterval`, `ErrorSource`; implement `DoWork`.
3. Register the worker with `services.AddHostedService<MyScraperWorker>()` from a `ServiceCollectionExtensions.Add<Module>Worker(this IServiceCollection)` method.
4. Add `builder.Services.Add<Module>Worker()` to [`Equibles.Worker.Host/Program.cs`](../../src/Equibles.Worker.Host/Program.cs).
5. If the scraper has tunables, define `<Module>ScraperOptions`, bind it in the host with `services.Configure<...>(builder.Configuration.GetSection("<Module>Scraper"))`, and inject `IOptions<...>` into the worker.
6. If the source is paginated → write a `Processed<X>` ledger table. If it's a continuous time series → use `SyncDateResolver.Resolve` against `max(date)`.
