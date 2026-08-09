using System.Net;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.HostedService.Configuration;
using Equibles.GovernmentContracts.Repositories;
using Equibles.Integrations.GovernmentContracts.Contracts;
using Equibles.Integrations.GovernmentContracts.Models;
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

    // How many windows in a row may fail before the scan treats the failure as source-wide and
    // aborts the cycle. Low enough that a fan-out costs a handful of Error rows instead of
    // thousands, high enough that a short run of genuinely bad days does not stop the scan.
    private const int MaxConsecutiveWindowFailures = 5;

    // USAspending rejects action-date searches earlier than its federal-award data epoch.
    private static readonly DateOnly UsaSpendingMinimumActionDate = new(2007, 10, 1);

    // Single well-known row that persists the forward award scan's resume point.
    private const string ScanStateName = "award-scan";

    // Version of the recipient-matching rules (normaliser + parent fallback). Unmatched
    // awards are dropped, not stored, so a matching improvement is invisible to history
    // until the range is re-walked: bump this when matching can newly resolve recipients it
    // previously dropped, and the next cycle pulls the cursor back to the epoch once
    // (deduplicated by AwardUniqueKey). Version 1 is the original exact-name matching;
    // 2 added the 2-character key floor, EDGAR slash-marker stripping, and SAM
    // parent-fallback resolution.
    internal const int RecipientMatchingVersion = 2;

    // Re-fetch a cached recipient-parent profile at point of use once it is older than
    // this — acquisitions move subsidiaries between listed parents, and a permanently
    // stale row would mis-attribute every future award. Encounter-driven, so recipients
    // that stop appearing cost nothing.
    private const int ParentCacheStalenessDays = 180;

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

        await ApplyMatchingVersionReset(cancellationToken);

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

        // The checkpoint records the CONTIGUOUS fully-scanned frontier, so it may only advance
        // while every window behind it has succeeded. On the first failure of a cycle the
        // frontier is pulled back behind that window and stops advancing: later windows still
        // run (their awards are ingested and deduplicated by AwardUniqueKey), but the resume
        // point stays behind the hole so the next cycle re-covers it. Leaving the frontier past
        // a failed window would silently lose its awards once it fell outside the trailing
        // rescan lookback.
        var frontierIsContiguous = true;

        // Nothing stepped over may fan out across the range. 422 is USAspending's validation
        // rejection for the WHOLE request, not just its dates, so a bad request-invariant field
        // (a filter shape, an award-type code, an out-of-range amount bound) 422s on every
        // window alike — and stepping over each of ~6,900 of them would write an Error row
        // apiece while the worker still saw a successful cycle. Past this many consecutive
        // window failures the pattern is the source, not the window: stop and let the worker's
        // failure streak back off. Counted consecutively so a scan pockmarked with isolated bad
        // days still completes.
        var consecutiveWindowFailures = 0;

        // Recipient-parent resolutions already read or fetched THIS cycle, so a recipient
        // appearing in many windows costs one profile lookup per cycle at most. Keyed by
        // USAspending's level-qualified recipient hash.
        var parentCache = new Dictionary<string, GovernmentContractRecipientParent>(
            StringComparer.Ordinal
        );

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
                    parentCache,
                    cancellationToken
                );

                // The window completed — advance the resumable checkpoint even when it
                // inserted nothing, so an empty or all-non-public window (and, above all, a
                // later transport abort in this same cycle) can't rewind the scan to here.
                // This is decoupled from MAX(ActionDate), which only moves on an insert.
                if (frontierIsContiguous)
                    await AdvanceCheckpoint(windowEnd);

                consecutiveWindowFailures = 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Government contracts import failed for window {Start}..{End}",
                    windowStart,
                    windowEnd
                );

                // Anything the classifier calls systemic says nothing about this window and
                // everything about the source: the remaining windows would fail identically,
                // and re-entering the client's retry ladder for each of them only hammers an
                // API that already told us to stop. Rethrow so the worker's consecutive-failure
                // streak owns reporting and backoff. Only a 422 — USAspending rejecting THIS
                // window's own date filters — is stepped over: report it, pull the frontier
                // back behind it, and scan the rest.
                if (
                    ex is HttpRequestException httpException
                    && IsSystemicHttpFailure(httpException)
                )
                {
                    _logger.LogWarning(
                        "Government contracts import: aborting this cycle after a systemic failure "
                            + "({StatusCode}); remaining windows will be retried on the next run",
                        httpException.StatusCode?.ToString() ?? "no response"
                    );
                    throw;
                }

                if (frontierIsContiguous)
                {
                    // The first hole this cycle. Windows run oldest-first, so this is the
                    // earliest unresolved day — pull the frontier back behind it and stop
                    // advancing for the rest of the cycle. Freezing alone is not enough: once
                    // the scan has caught up, the checkpoint LEADS the trailing-rescan window,
                    // so a failure inside that window is already behind the frontier and the
                    // next cycle would step straight over it as the lookback slid forward.
                    frontierIsContiguous = false;
                    await RetractCheckpoint(windowStart.AddDays(-1));
                }

                await _errorReporter.Report(
                    ErrorSource.GovernmentContractsScraper,
                    "GovernmentContractsImport.ImportWindow",
                    ex,
                    $"window: {windowStart}..{windowEnd}"
                );

                if (++consecutiveWindowFailures >= MaxConsecutiveWindowFailures)
                {
                    _logger.LogWarning(
                        "Government contracts import: aborting this cycle after {Count} "
                            + "consecutive window failures; the failure is source-wide, not "
                            + "window-specific",
                        consecutiveWindowFailures
                    );
                    throw;
                }
            }
        }

        _logger.LogInformation(
            "Government contracts import complete: {Count} new awards persisted",
            totalInserted
        );
    }

    /// <summary>
    /// Does this HTTP failure condemn the whole cycle, or only the window that produced it?
    ///
    /// Systemic is the DEFAULT, and deliberately so. Stepping over a failure only makes sense
    /// when we know it is about the dates we asked for; for anything else, continuing fans the
    /// same failure out across every remaining window — up to ~6,900 of them from the 2007
    /// epoch at the default one-day width — writing an Error row each time while the source is
    /// telling us to stop. A systemic abort instead hands the outage to the worker's
    /// consecutive-failure streak, which reports once and backs off.
    ///
    /// 422 is the sole request-level exit: it is USAspending's validation rejection of the
    /// window's own filters, the failure this classifier exists for (an action-date range below
    /// the 2007-10-01 epoch). 400 stays systemic even though it reads "malformed" — the fields
    /// it most likely refers to (filter shape, award-type codes) are identical in every window,
    /// so treating it as window-level would fan out a source-wide defect.
    /// </summary>
    private static bool IsSystemicHttpFailure(HttpRequestException exception) =>
        exception.StatusCode != HttpStatusCode.UnprocessableEntity;

    private async Task<int> ImportWindow(
        DateOnly windowStart,
        DateOnly windowEnd,
        IReadOnlyDictionary<string, Guid> recipientLookup,
        Dictionary<string, GovernmentContractRecipientParent> parentCache,
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
        // Direct name matches first; recipients whose own name matched nothing get a second
        // chance through their SAM-registered parent (operating subsidiaries — "CACI Inc -
        // Federal" — award under names our CommonStock universe never carries).
        var unmatched = new List<UsaSpendingAwardRecord>();
        var mapped = new Dictionary<string, GovernmentContract>(StringComparer.Ordinal);
        foreach (var award in awards)
        {
            var commonStockId = RecipientResolver.Resolve(award.RecipientName, recipientLookup);
            if (commonStockId == null)
            {
                if (award.RecipientId != null)
                    unmatched.Add(award);
                continue;
            }

            var entity = UsaSpendingAwardMapper.Map(award, commonStockId.Value);
            if (entity != null)
                mapped[entity.AwardUniqueKey] = entity;
        }

        if (unmatched.Count > 0)
        {
            // A profile fetch that fails transport-level propagates out of here and fails
            // the WINDOW, exactly like the award search itself — the checkpoint machinery
            // then re-covers it, so an outage can never silently drop this window's
            // parent-resolved awards. A 404 never reaches this path (the client answers it
            // as a null profile, cached as parentless).
            await ResolveParents(unmatched, parentCache, cancellationToken);

            foreach (var award in unmatched)
            {
                if (!parentCache.TryGetValue(award.RecipientId, out var parent))
                    continue;
                var commonStockId = RecipientResolver.ResolveViaParents(
                    [parent.ParentName],
                    recipientLookup
                );
                if (commonStockId == null)
                    continue;

                var entity = UsaSpendingAwardMapper.Map(award, commonStockId.Value);
                if (entity != null)
                    mapped[entity.AwardUniqueKey] = entity;
            }
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

    /// <summary>
    /// Ensure every unmatched award's recipient has a parent-resolution row in
    /// <paramref name="parentCache"/>: stored rows are read in one batch, and recipients
    /// with no stored row (or a stale one, per <see cref="ParentCacheStalenessDays"/>) get
    /// one profile fetch each, persisted so the answer — including "no usable parent" —
    /// survives across cycles. The fetch runs inside the caller's window try/catch on
    /// purpose: a transport failure here must fail the window, not skip the recipient.
    /// </summary>
    private async Task ResolveParents(
        IReadOnlyList<UsaSpendingAwardRecord> unmatched,
        Dictionary<string, GovernmentContractRecipientParent> parentCache,
        CancellationToken cancellationToken
    )
    {
        var now = DateTime.UtcNow;
        var staleBefore = now.AddDays(-ParentCacheStalenessDays);
        var pending = unmatched
            .Where(a => a.RecipientId != null && !parentCache.ContainsKey(a.RecipientId))
            .GroupBy(a => a.RecipientId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().RecipientName, StringComparer.Ordinal);
        if (pending.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var repository =
            scope.ServiceProvider.GetRequiredService<GovernmentContractRecipientParentRepository>();

        var stored = await repository.GetByRecipientIds(pending.Keys.ToList(), cancellationToken);
        foreach (var row in stored.Where(r => r.ResolvedAt >= staleBefore))
        {
            parentCache[row.RecipientId] = row;
            pending.Remove(row.RecipientId);
        }
        var staleRows = stored
            .Where(r => r.ResolvedAt < staleBefore)
            .ToDictionary(r => r.RecipientId, StringComparer.Ordinal);

        if (pending.Count == 0)
            return;

        var persisted = 0;
        foreach (var (recipientId, recipientName) in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var profile = await _client.GetRecipientProfile(recipientId, cancellationToken);
            var parent = ChooseParent(profile, recipientId);

            if (staleRows.TryGetValue(recipientId, out var row))
            {
                row.ParentRecipientId = parent?.ParentId;
                row.ParentName = parent?.ParentName;
                row.ResolvedAt = now;
                repository.Update(row);
            }
            else
            {
                row = new GovernmentContractRecipientParent
                {
                    RecipientId = recipientId,
                    RecipientName = Truncate(recipientName, 512),
                    ParentRecipientId = parent?.ParentId,
                    ParentName = Truncate(parent?.ParentName, 512),
                    ResolvedAt = now,
                };
                repository.Add(row);
            }

            parentCache[recipientId] = row;
            persisted++;
        }

        if (persisted > 0)
            await repository.SaveChanges();
    }

    /// <summary>
    /// Pick the ONE parent a recipient profile supports, or null. The top-level
    /// parent_id/parent_name pair is USAspending's current SAM linkage and wins when
    /// present; otherwise the parents[] history is used only when it names a single
    /// distinct parent — several distinct parents means ownership moved and guessing
    /// between them could assert a wrong link. A parent that IS the recipient itself
    /// (parent-level recipients self-reference) carries no new name to match and is
    /// treated as no parent.
    /// </summary>
    internal static UsaSpendingRecipientParentRef ChooseParent(
        UsaSpendingRecipientProfile profile,
        string recipientId
    )
    {
        if (profile == null)
            return null;

        var candidate =
            !string.IsNullOrWhiteSpace(profile.ParentId)
            && !string.IsNullOrWhiteSpace(profile.ParentName)
                ? new UsaSpendingRecipientParentRef
                {
                    ParentId = profile.ParentId,
                    ParentName = profile.ParentName,
                }
                : null;

        if (candidate == null)
        {
            var distinct = (profile.Parents ?? [])
                .Where(p =>
                    !string.IsNullOrWhiteSpace(p?.ParentId)
                    && !string.IsNullOrWhiteSpace(p.ParentName)
                )
                .GroupBy(p => p.ParentId, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();
            if (distinct.Count == 1)
                candidate = distinct[0];
        }

        if (candidate == null || candidate.ParentId == recipientId)
            return null;

        return candidate;
    }

    private static string Truncate(string value, int maxLength) =>
        value != null && value.Length > maxLength ? value[..maxLength] : value;

    /// <summary>
    /// Pull the scan cursor back to the epoch ONCE when the recipient-matching code is
    /// newer than the version the stored range was matched under. Unmatched awards are
    /// dropped rather than stored, so this rescan is the only way a matching improvement
    /// reaches history; everything re-fetched deduplicates by AwardUniqueKey. Cursor and
    /// version stamp move in one save — a crash between cycles just re-triggers the same
    /// idempotent rescan. Unlike the checkpoint writes this is NOT best-effort: failing to
    /// persist must fail the cycle, or the scan would advance and re-stamp under the new
    /// version without ever having re-walked history.
    /// </summary>
    private async Task ApplyMatchingVersionReset(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var repository =
            scope.ServiceProvider.GetRequiredService<GovernmentContractsScanStateRepository>();

        var state = await repository.GetByName(ScanStateName);
        if (state == null || state.MatchingVersion >= RecipientMatchingVersion)
            return;

        _logger.LogInformation(
            "Government contracts import: recipient matching upgraded (v{From} -> v{To}); "
                + "rescanning from the {Epoch} epoch to recover previously unmatched awards",
            state.MatchingVersion,
            RecipientMatchingVersion,
            UsaSpendingMinimumActionDate
        );

        state.LastCompletedWindowEnd = UsaSpendingMinimumActionDate.AddDays(-1);
        state.MatchingVersion = RecipientMatchingVersion;
        state.UpdatedAt = DateTime.UtcNow;
        repository.Update(state);
        await repository.SaveChanges();
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
    /// Once a checkpoint exists it SOLELY owns the cursor. The scan resumes the day after the
    /// furthest contiguous window it has fully completed, but never later than a trailing
    /// <paramref name="rescanLookbackDays"/> window, so awards USAspending publishes late
    /// (dated inside a window already passed) are still re-covered and deduplicated by
    /// AwardUniqueKey on insert. A consequence is that once caught up the scan no longer
    /// reports "already up to date"; it re-scans the trailing window each cycle.
    ///
    /// <paramref name="latestActionDate"/> deliberately does NOT pull the cursor forward here.
    /// A cycle that fails one window keeps scanning the later ones, so ingested data routinely
    /// leads the frontier — letting MAX(ActionDate) win would step straight over the failed
    /// window and lose its awards for good. It still seeds the cold-start path below, where no
    /// checkpoint exists to be trusted.
    ///
    /// Every resolution path is clamped to USAspending's 2007-10-01 action-date epoch without
    /// lowering a later cursor.
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

        DateOnly resolvedStartDate;
        if (checkpointEnd == null)
        {
            resolvedStartDate = SyncDateResolver.Resolve(
                latestActionDate ?? default,
                workerOptions
            );
        }
        else
        {
            // Resume after the furthest point we have CONTIGUOUSLY scanned. See the remarks
            // above for why an ingested action date past this point must not drag it forward.
            var afterFrontier = checkpointEnd.Value.AddDays(1);
            var lookbackFloor = today.AddDays(-(Math.Max(1, rescanLookbackDays) - 1));
            resolvedStartDate = afterFrontier < lookbackFloor ? afterFrontier : lookbackFloor;
        }

        return resolvedStartDate < UsaSpendingMinimumActionDate
            ? UsaSpendingMinimumActionDate
            : resolvedStartDate;
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
                // A first row is born current: whatever range this install scans, it scans
                // under today's matching rules, so stamping the version here keeps a fresh
                // install from triggering the version-reset epoch rescan on its next cycle.
                state = new GovernmentContractsScanState
                {
                    Name = ScanStateName,
                    MatchingVersion = RecipientMatchingVersion,
                };
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

    /// <summary>
    /// Pull the persisted checkpoint BACK to <paramref name="windowEnd"/> when it currently
    /// leads that day, so the next cycle resumes at the window that just failed. The mirror of
    /// <see cref="AdvanceCheckpoint"/>, and the only path allowed to lower the frontier.
    ///
    /// Needed because a caught-up scan re-covers a trailing lookback window that sits BEHIND
    /// the checkpoint: a failure there is already inside the scanned region, so merely
    /// declining to advance would leave the frontier untouched and the next cycle's lookback
    /// would slide past the failed day, losing any award published for it late. Best-effort
    /// like its counterpart — a persist failure only costs the retry, never the cycle.
    /// </summary>
    private async Task RetractCheckpoint(DateOnly windowEnd)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository =
                scope.ServiceProvider.GetRequiredService<GovernmentContractsScanStateRepository>();

            var state = await repository.GetByName(ScanStateName);

            // No row yet is NOT "nothing to do". Without one the next cycle falls back to the
            // cold-start cursor, which resolves off MAX(ActionDate) — and the later windows of
            // this very cycle are still running and inserting rows past the failure. Leaving
            // the row absent would let that watermark carry the scan straight over the failed
            // window. Create it here so the hole survives to the next cycle.
            if (state == null)
            {
                _logger.LogWarning(
                    "Government contracts import: opening the scan checkpoint at {To} so the "
                        + "failed first window is re-covered next cycle",
                    windowEnd
                );
                repository.Add(
                    new GovernmentContractsScanState
                    {
                        Name = ScanStateName,
                        LastCompletedWindowEnd = windowEnd,
                        UpdatedAt = DateTime.UtcNow,
                        MatchingVersion = RecipientMatchingVersion,
                    }
                );
                await repository.SaveChanges();
                return;
            }

            if (state.LastCompletedWindowEnd <= windowEnd)
                return;

            _logger.LogWarning(
                "Government contracts import: pulling the scan checkpoint back from {From} to "
                    + "{To} so the failed window is re-covered next cycle",
                state.LastCompletedWindowEnd,
                windowEnd
            );

            state.LastCompletedWindowEnd = windowEnd;
            state.UpdatedAt = DateTime.UtcNow;
            repository.Update(state);
            await repository.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Government contracts import: failed to retract the scan checkpoint to "
                    + "{WindowEnd}; the failed window may not be re-covered",
                windowEnd
            );
        }
    }
}
