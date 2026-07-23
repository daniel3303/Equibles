using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.HostedService.Configuration;
using Equibles.GovernmentContracts.Repositories;
using Equibles.Integrations.GovernmentContracts.Contracts;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.GovernmentContracts.HostedService.Services;

[Service]
public class GovernmentContractsImportService : IImporter
{
    private const int InsertBatchSize = 1000;

    // Single well-known row that persists the forward award scan's resume point.
    private const string ScanStateName = "award-scan";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GovernmentContractsImportService> _logger;
    private readonly IUsaSpendingClient _client;
    private readonly RecipientResolver _resolver;
    private readonly GovernmentContractsScraperOptions _options;
    private readonly WorkerOptions _workerOptions;
    private readonly ErrorReporter _errorReporter;

    public GovernmentContractsImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<GovernmentContractsImportService> logger,
        IUsaSpendingClient client,
        RecipientResolver resolver,
        IOptions<GovernmentContractsScraperOptions> options,
        IOptions<WorkerOptions> workerOptions,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _client = client;
        _resolver = resolver;
        _options = options.Value;
        _workerOptions = workerOptions.Value;
        _errorReporter = errorReporter;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        var recipientLookup = await _resolver.BuildLookup(cancellationToken);
        if (recipientLookup.Count == 0)
        {
            _logger.LogWarning(
                "Government contracts import: the CommonStock universe is empty; skipping cycle"
            );
            return;
        }

        var startDate = await DetermineStartDate(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (startDate > today)
        {
            _logger.LogInformation("Government contracts import: already up to date");
            return;
        }

        _logger.LogInformation(
            "Government contracts import: scanning {Start}..{Today} (>= ${Min:N0}) against {Count} companies",
            startDate,
            today,
            _options.MinimumAwardAmount,
            recipientLookup.Count
        );

        var windowDays = Math.Max(1, _options.WindowDays);
        var totalInserted = 0;

        for (
            var windowStart = startDate;
            windowStart <= today;
            windowStart = windowStart.AddDays(windowDays)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var windowEnd = windowStart.AddDays(windowDays - 1);
            if (windowEnd > today)
                windowEnd = today;

            try
            {
                totalInserted += await ImportWindow(
                    windowStart,
                    windowEnd,
                    recipientLookup,
                    cancellationToken
                );

                // The window completed — advance the resumable checkpoint even when it
                // inserted nothing, so an empty or all-non-public window (and, above all, a
                // later transport abort in this same cycle) can't rewind the scan to here.
                // This is decoupled from MAX(ActionDate), which only moves on an insert.
                await AdvanceCheckpoint(windowEnd);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Government contracts import failed for window {Start}..{End}",
                    windowStart,
                    windowEnd
                );

                // A transport-level failure (the API is unreachable, even after the client's retries)
                // is systemic: every remaining window would fail identically. Rethrow so the worker's
                // consecutive-failure streak owns the reporting — it records ONE Error row per outage
                // (once the streak reaches its threshold) and backs off, instead of this loop writing
                // a fresh row every cycle for the same unreachable API. The next cycle resumes from
                // the same start date. Window-specific failures are reported here and the scan
                // continues to the remaining windows.
                if (ex is HttpRequestException)
                {
                    _logger.LogWarning(
                        "Government contracts import: aborting this cycle after a transport failure; "
                            + "remaining windows will be retried on the next run"
                    );
                    throw;
                }

                await _errorReporter.Report(
                    ErrorSource.GovernmentContractsScraper,
                    "GovernmentContractsImport.ImportWindow",
                    ex,
                    $"window: {windowStart}..{windowEnd}"
                );
            }
        }

        _logger.LogInformation(
            "Government contracts import complete: {Count} new awards persisted",
            totalInserted
        );
    }

    private async Task<int> ImportWindow(
        DateOnly windowStart,
        DateOnly windowEnd,
        IReadOnlyDictionary<string, Guid> recipientLookup,
        CancellationToken cancellationToken
    )
    {
        var awards = await _client.GetContractAwards(
            windowStart,
            windowEnd,
            _options.MinimumAwardAmount,
            cancellationToken
        );
        if (awards.Count == 0)
            return 0;

        // Resolve to public companies, map, and de-duplicate within the batch by unique key.
        var mapped = new Dictionary<string, GovernmentContract>(StringComparer.Ordinal);
        foreach (var award in awards)
        {
            var commonStockId = RecipientResolver.Resolve(award.RecipientName, recipientLookup);
            if (commonStockId == null)
                continue;

            var entity = UsaSpendingAwardMapper.Map(award, commonStockId.Value);
            if (entity != null)
                mapped[entity.AwardUniqueKey] = entity;
        }

        if (mapped.Count == 0)
            return 0;

        var existingKeys = await LoadExistingKeys(mapped.Keys, cancellationToken);
        var toInsert = mapped.Values.Where(c => !existingKeys.Contains(c.AwardUniqueKey)).ToList();
        if (toInsert.Count == 0)
            return 0;

        var inserted = await BatchPersister.Persist<
            GovernmentContract,
            GovernmentContractRepository
        >(toInsert, InsertBatchSize, _scopeFactory);

        _logger.LogDebug(
            "Government contracts {Start}..{End}: {Inserted} new of {Matched} matched ({Fetched} fetched)",
            windowStart,
            windowEnd,
            inserted,
            mapped.Count,
            awards.Count
        );
        return inserted;
    }

    private async Task<HashSet<string>> LoadExistingKeys(
        IEnumerable<string> keys,
        CancellationToken cancellationToken
    )
    {
        var keyList = keys.ToList();
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<GovernmentContractRepository>();
        return (
            await repository
                .GetAll()
                .Where(c => keyList.Contains(c.AwardUniqueKey))
                .Select(c => c.AwardUniqueKey)
                .ToListAsync(cancellationToken)
        ).ToHashSet(StringComparer.Ordinal);
    }

    private async Task<DateOnly> DetermineStartDate(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<GovernmentContractRepository>();
        var scanStateRepository =
            scope.ServiceProvider.GetRequiredService<GovernmentContractsScanStateRepository>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Future action dates are excluded from the watermark: a single mis-dated row would
        // otherwise push the resume point past today and freeze the import forever (this
        // happened in prod when period-of-performance starts were stored as ActionDate).
        var latest = await repository
            .GetAll()
            .Where(c => c.ActionDate != null && c.ActionDate <= today)
            .MaxAsync(c => c.ActionDate, cancellationToken);

        var checkpoint = await scanStateRepository.GetByName(ScanStateName);

        return ResolveStartDate(
            latest,
            checkpoint?.LastCompletedWindowEnd,
            today,
            _options.RescanLookbackDays,
            _workerOptions
        );
    }

    /// <summary>
    /// Resolve the date the next scan cycle starts from. Pure so the cursor policy is
    /// unit-testable.
    ///
    /// With no persisted checkpoint (the first run after this ships, or a fresh install) the
    /// cursor falls back to the data-derived watermark exactly as before: the day after the
    /// newest credible action date, or the configured floor when the table is empty.
    ///
    /// Once a checkpoint exists it owns the cursor. The scan resumes the day after the
    /// furthest window it has fully completed — never behind data already ingested — but
    /// never later than a trailing <paramref name="rescanLookbackDays"/> window, so awards
    /// USAspending publishes late (dated inside a window already passed) are still re-covered
    /// and deduplicated by AwardUniqueKey on insert. A consequence is that once caught up the
    /// scan no longer reports "already up to date"; it re-scans the trailing window each cycle.
    /// </summary>
    public static DateOnly ResolveStartDate(
        DateOnly? latestActionDate,
        DateOnly? checkpointEnd,
        DateOnly today,
        int rescanLookbackDays,
        WorkerOptions workerOptions
    )
    {
        if (latestActionDate > today)
            latestActionDate = today;

        if (checkpointEnd == null)
            return SyncDateResolver.Resolve(latestActionDate ?? default, workerOptions);

        // Resume after the furthest point we have fully scanned, but never behind data
        // already ingested (defensive — the checkpoint should always lead the watermark).
        var frontier = checkpointEnd.Value;
        if (latestActionDate.HasValue && latestActionDate.Value > frontier)
            frontier = latestActionDate.Value;

        var afterFrontier = frontier.AddDays(1);
        var lookbackFloor = today.AddDays(-(Math.Max(1, rescanLookbackDays) - 1));
        return afterFrontier < lookbackFloor ? afterFrontier : lookbackFloor;
    }

    /// <summary>
    /// Advance the persisted scan checkpoint to <paramref name="windowEnd"/>. Best-effort by
    /// design — a persist failure must never fail the scan; the window is simply re-scanned
    /// next cycle and deduplicated by AwardUniqueKey. The write is monotonic so a trailing
    /// lookback rescan (which re-completes earlier windows) can never rewind the frontier.
    /// </summary>
    private async Task AdvanceCheckpoint(DateOnly windowEnd)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository =
                scope.ServiceProvider.GetRequiredService<GovernmentContractsScanStateRepository>();

            var state = await repository.GetByName(ScanStateName);
            var isNew = state == null;
            if (isNew)
            {
                state = new GovernmentContractsScanState { Name = ScanStateName };
            }
            else if (state.LastCompletedWindowEnd >= windowEnd)
            {
                return;
            }

            state.LastCompletedWindowEnd = windowEnd;
            state.UpdatedAt = DateTime.UtcNow;
            if (isNew)
                repository.Add(state);
            else
                repository.Update(state);
            await repository.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Government contracts import: failed to persist scan checkpoint at {WindowEnd}; "
                    + "the window will be re-scanned next cycle",
                windowEnd
            );
        }
    }
}
