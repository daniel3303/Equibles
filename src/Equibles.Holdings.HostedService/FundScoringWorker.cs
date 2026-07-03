using Equibles.CommonStocks.Data.Helpers;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Periodically (re)computes the fund score for filers that have holdings on file, so the
/// institutions leaderboard can rank the universe by alpha vs the benchmark. Scoring is
/// incremental: a filer is re-scored when holdings data was imported after its last score,
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

    // The incremental selection: one grouped pass over the holdings table yields the filer
    // universe together with each filer's latest data-import time (row CreationTime — exact,
    // unlike FilingDate, which a late backfill of old filings would not bump), and one small
    // read yields the existing scores' last-computed times. A filer is due when it has no
    // score for this (window, benchmark), its data changed after the score, or the score has
    // aged past MaxScoreAge. FundScore.CreationTime is refreshed on every upsert, so it is
    // the "last scored at" marker; a transiently unscoreable filer keeps its old timestamps
    // and is naturally retried next cycle.
    private async Task<List<Guid>> SelectHoldersNeedingScore(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var holders = await dbContext
            .Set<InstitutionalHolding>()
            .GroupBy(h => h.InstitutionalHolderId)
            .Select(g => new { HolderId = g.Key, LastImported = g.Max(h => h.CreationTime) })
            .ToListAsync(cancellationToken);

        var benchmark = TickerNormalizer.Normalize(BenchmarkTicker);
        var scoredAt = await dbContext
            .Set<FundScore>()
            .Where(s => s.WindowYears == WindowYears && s.BenchmarkTicker == benchmark)
            .Select(s => new { s.InstitutionalHolderId, s.CreationTime })
            .ToDictionaryAsync(
                s => s.InstitutionalHolderId,
                s => s.CreationTime,
                cancellationToken
            );

        var staleBefore = DateTime.UtcNow - MaxScoreAge;
        var pending = holders
            .Where(h =>
                IsScoreDue(
                    scoredAt.TryGetValue(h.HolderId, out var lastScored)
                        ? lastScored
                        : (DateTime?)null,
                    h.LastImported,
                    staleBefore
                )
            )
            .Select(h => h.HolderId)
            .ToList();

        _logger.LogInformation(
            "Fund scoring cycle: {Pending} of {Total} filer(s) due (new data, stale, or unscored)",
            pending.Count,
            holders.Count
        );
        return pending;
    }

    // The pure due-decision behind the incremental cycle: unscored filers are always due;
    // scored filers are due when new holdings data was imported after the score, or the
    // score has aged past the staleness floor. Internal so tests can pin the rule directly.
    internal static bool IsScoreDue(
        DateTime? lastScoredAt,
        DateTime lastImportedAt,
        DateTime staleBefore
    ) =>
        lastScoredAt is not { } lastScored
        || lastScored < staleBefore
        || lastImportedAt > lastScored;

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
}
