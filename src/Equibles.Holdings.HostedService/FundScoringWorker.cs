using Equibles.CommonStocks.Data.Helpers;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Periodically (re)computes the fund score for filers with a filing rollup or an existing
/// score, so the institutions leaderboard can rank the universe by alpha vs the benchmark.
/// Scoring is incremental: a filer is re-scored when a filing was imported after its last score,
/// when its score is older than <see cref="MaxScoreAge"/>, or when it has no score at all
/// (which also keeps visiting Schedule 13D/G-only filers — scoring them yields nothing, which
/// prunes any stale score they may have accumulated, and their backtest short-circuits before
/// loading prices). Re-scoring every filer daily ran ~15k multi-year price-history backtests
/// per day — billions of price rows read for scores whose portfolios only change when a new
/// 13F lands, while the price-only drift of a rolling multi-year alpha is far too slow for a
/// daily recompute to reorder the leaderboard.
/// <para>
/// Each filer is scored in its own DI scope: the backtest can load thousands of price rows, so a
/// single shared context's change tracker would balloon across the run, and a fault scoring one
/// filer stays contained instead of aborting the whole cycle.
/// </para>
/// </summary>
public class FundScoringWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FundScoringWorker> _logger;

    // Virtual seams so tests can collapse the waits without changing production behaviour.
    protected virtual TimeSpan StartupDelay => TimeSpan.FromMinutes(10);
    protected virtual TimeSpan SleepInterval => TimeSpan.FromHours(24);
    protected virtual int WindowYears => FundScoringManager.DefaultWindowYears;
    protected virtual string BenchmarkTicker => FundScoringManager.DefaultBenchmark;

    // Price-drift refresh floor: with no new filing, a filer's rolling-window alpha still
    // drifts as the window slides over daily prices, so every score is recomputed at least
    // this often. A week keeps the leaderboard honest while spreading the recompute cost to
    // ~1/7 of the universe per daily cycle.
    protected virtual TimeSpan MaxScoreAge => TimeSpan.FromDays(7);

    public FundScoringWorker(IServiceScopeFactory scopeFactory, ILogger<FundScoringWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (StartupDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(StartupDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var scored = await ScoreAllHolders(stoppingToken);
                _logger.LogInformation(
                    "Fund scoring cycle complete: scored {Count} filer(s)",
                    scored
                );
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fund scoring cycle failed; will retry next cycle");
            }

            try
            {
                await Task.Delay(SleepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Scores every filer whose score is missing, stale, or older than its latest imported
    /// holdings data, returning how many produced a score. Internal so the worker's batch
    /// behaviour can be driven directly in tests.
    /// </summary>
    internal async Task<int> ScoreAllHolders(CancellationToken cancellationToken)
    {
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        // Without the benchmark no filer can score, and stamping every visit would hold the whole
        // universe for MaxScoreAge after it heals, so the cycle visits nobody instead.
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<FundScoringManager>();
            if (!await manager.CanScoreAgainst(asOf, WindowYears, BenchmarkTicker))
            {
                _logger.LogWarning(
                    "Fund scoring cycle skipped: benchmark {Benchmark} has no issuer or no prices in the window",
                    BenchmarkTicker
                );
                return 0;
            }
        }
        var pending = await SelectHoldersNeedingScore(cancellationToken);

        var scored = 0;
        foreach (var holderId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ScoreHolder(holderId, asOf, cancellationToken))
                scored++;
        }
        return scored;
    }

    // A filer is due when it has no score for this (window, benchmark), its data changed after
    // FundScore.CreationTime (the last-scored marker), or the score aged past MaxScoreAge.
    // Both change signals come from the per-accession InstitutionalFiling rollups (CreationTime
    // for new accessions, the latest FilingDate for in-place amendments), which SyncFilingSummaries
    // writes after a data set's positions, so a filer whose positions lead its rollup is scored
    // once the rollup lands.
    internal async Task<List<Guid>> SelectHoldersNeedingScore(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var holders = await dbContext
            .Set<InstitutionalFiling>()
            .GroupBy(f => f.InstitutionalHolderId)
            .Select(g => new
            {
                HolderId = g.Key,
                LastImported = g.Max(f => f.CreationTime),
                LastFiled = g.Max(f => f.FilingDate),
            })
            .ToListAsync(cancellationToken);

        // The stored key is the manager's dash-listed form, so both stamps must be read with it.
        var benchmark = TickerNormalizer.NormalizeDashListed(BenchmarkTicker);
        if (benchmark == null)
            throw new InvalidOperationException(
                "The configured fund-scoring benchmark is invalid."
            );

        var scoreStates = await dbContext
            .Set<FundScore>()
            .Where(s => s.WindowYears == WindowYears && s.BenchmarkTicker == benchmark)
            .Select(s => new
            {
                s.InstitutionalHolderId,
                s.CreationTime,
                s.CalculationVersion,
            })
            .ToDictionaryAsync(s => s.InstitutionalHolderId, cancellationToken);
        var attempts = await dbContext
            .Set<FundScoreAttempt>()
            .Where(a => a.WindowYears == WindowYears && a.BenchmarkTicker == benchmark)
            .Select(a => new
            {
                a.InstitutionalHolderId,
                a.AttemptedAt,
                a.CalculationVersion,
            })
            .ToDictionaryAsync(a => a.InstitutionalHolderId, cancellationToken);

        // A joint filing's shared accession rolls up under one holder only, so a scored co-filer
        // with no rollup of its own is still revisited on the staleness floor and refreshed or pruned.
        var discovered = holders.Select(h => h.HolderId).ToHashSet();
        var candidates = holders
            .Select(h => (h.HolderId, h.LastImported, h.LastFiled))
            .Concat(
                scoreStates
                    .Keys.Where(id => !discovered.Contains(id))
                    .Select(id =>
                        (
                            HolderId: id,
                            LastImported: DateTime.MinValue,
                            LastFiled: DateOnly.MinValue
                        )
                    )
            )
            .ToList();

        var staleBefore = DateTime.UtcNow - MaxScoreAge;
        var pending = candidates
            .Select(h =>
            {
                scoreStates.TryGetValue(h.HolderId, out var scoreState);
                attempts.TryGetValue(h.HolderId, out var attempt);
                // An unscored filer's last visit stands in for the score it has none of, so an
                // unscoreable filer waits the staleness floor instead of being re-read every cycle.
                var lastScored = scoreState?.CreationTime ?? attempt?.AttemptedAt;
                var version = scoreState?.CalculationVersion ?? attempt?.CalculationVersion;
                return (
                    h.HolderId,
                    // Every visit refreshes the attempt stamp, so a scored filer whose backtest
                    // keeps refusing does not sit at the head under its old score.
                    LastVisit: attempt?.AttemptedAt ?? scoreState?.CreationTime,
                    Due: IsScoreDue(lastScored, version, h.LastImported, h.LastFiled, staleBefore)
                );
            })
            .Where(h => h.Due)
            // Never-visited filers first, then the longest unvisited, so a cycle a restart cuts
            // short resumes where the previous one stopped instead of re-reading its head.
            .OrderBy(h => h.LastVisit ?? DateTime.MinValue)
            .Select(h => h.HolderId)
            .ToList();

        _logger.LogInformation(
            "Fund scoring cycle: {Pending} of {Total} filer(s) due (new data, stale, or unscored)",
            pending.Count,
            candidates.Count
        );
        return pending;
    }

    // The pure due-decision behind the incremental cycle: unscored filers are always due;
    // scored filers are due when new holdings data was imported after the score, when the
    // latest filing is dated on or after the score's day (in-place amendment restatements
    // rewrite rows without a new CreationTime, so the filing date is the only tell — using
    // its end-of-day costs at most one extra re-score right after a filing lands), or when
    // the score has aged past the staleness floor. Internal so tests can pin the rule.
    internal static bool IsScoreDue(
        DateTime? lastScoredAt,
        int? calculationVersion,
        DateTime lastImportedAt,
        DateOnly lastFiledOn,
        DateTime staleBefore
    ) =>
        calculationVersion != FundScore.CurrentCalculationVersion
        || lastScoredAt is not { } lastScored
        || lastScored < staleBefore
        || lastImportedAt > lastScored
        || lastFiledOn.ToDateTime(TimeOnly.MaxValue) > lastScored;

    private async Task<bool> ScoreHolder(
        Guid holderId,
        DateOnly asOf,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var holderRepository =
                scope.ServiceProvider.GetRequiredService<InstitutionalHolderRepository>();
            var manager = scope.ServiceProvider.GetRequiredService<FundScoringManager>();

            var holder = await holderRepository.Get(holderId);
            if (holder == null)
                return false;

            var score = await manager.ScoreHolder(holder, asOf, WindowYears, BenchmarkTicker);
            await RecordAttempt(scope.ServiceProvider, holderId, cancellationToken);
            return score != null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to score filer {HolderId}; skipping", holderId);
            return false;
        }
    }

    // Stamped after every visit, scored or not; a visit that faulted is left unstamped so the
    // next cycle retries it.
    private async Task RecordAttempt(
        IServiceProvider services,
        Guid holderId,
        CancellationToken cancellationToken
    )
    {
        var dbContext = services.GetRequiredService<EquiblesFinancialDbContext>();
        var benchmark = TickerNormalizer.NormalizeDashListed(BenchmarkTicker);
        var attempt = await dbContext
            .Set<FundScoreAttempt>()
            .SingleOrDefaultAsync(
                a =>
                    a.InstitutionalHolderId == holderId
                    && a.WindowYears == WindowYears
                    && a.BenchmarkTicker == benchmark,
                cancellationToken
            );
        if (attempt == null)
        {
            attempt = new FundScoreAttempt
            {
                InstitutionalHolderId = holderId,
                WindowYears = WindowYears,
                BenchmarkTicker = benchmark,
            };
            dbContext.Set<FundScoreAttempt>().Add(attempt);
        }
        attempt.AttemptedAt = DateTime.UtcNow;
        attempt.CalculationVersion = FundScore.CurrentCalculationVersion;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
