using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Periodically (re)computes the fund score for every filer that has 13F holdings, so the
/// institutions leaderboard can rank the universe by alpha vs the benchmark. A score depends on
/// daily prices as well as quarterly 13F filings, so the job refreshes daily even though the
/// underlying portfolio only changes each quarter.
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
    /// Scores every filer with 13F holdings as of today, returning how many produced a score.
    /// Internal so the worker's batch behaviour can be driven directly in tests.
    /// </summary>
    internal async Task<int> ScoreAllHolders(CancellationToken cancellationToken)
    {
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var holderIds = await LoadScoreableHolderIds(cancellationToken);

        var scored = 0;
        foreach (var holderId in holderIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ScoreHolder(holderId, asOf, cancellationToken))
                scored++;
        }
        return scored;
    }

    private async Task<List<Guid>> LoadScoreableHolderIds(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        return await dbContext
            .Set<InstitutionalHolding>()
            .Select(h => h.InstitutionalHolderId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

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
