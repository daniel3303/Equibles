using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class FundScoreRepository : BaseRepository<FundScore>
{
    public FundScoreRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>The current score for one filer in a given window/benchmark, or null if not scored yet.</summary>
    public async Task<FundScore> GetByHolder(
        InstitutionalHolder holder,
        int windowYears,
        string benchmarkTicker
    )
    {
        return await GetAll()
            .FirstOrDefaultAsync(s =>
                s.InstitutionalHolderId == holder.Id
                && s.WindowYears == windowYears
                && s.BenchmarkTicker == benchmarkTicker
                && s.CalculationVersion == FundScore.CurrentCalculationVersion
            );
    }

    /// <summary>
    /// The row for an upsert regardless of calculation version. Older versions are updated in
    /// place so the unique (holder, window, benchmark) key never conflicts during a basis rollout.
    /// </summary>
    public async Task<FundScore> GetByHolderForUpdate(
        InstitutionalHolder holder,
        int windowYears,
        string benchmarkTicker
    )
    {
        return await GetAll()
            .FirstOrDefaultAsync(s =>
                s.InstitutionalHolderId == holder.Id
                && s.WindowYears == windowYears
                && s.BenchmarkTicker == benchmarkTicker
            );
    }

    /// <summary>All current scores for one filer, latest window/benchmark variants included.</summary>
    public IQueryable<FundScore> GetByHolder(InstitutionalHolder holder)
    {
        return GetAll()
            .Where(s =>
                s.InstitutionalHolderId == holder.Id
                && s.CalculationVersion == FundScore.CurrentCalculationVersion
            );
    }

    /// <summary>
    /// Scores for a given window/benchmark ranked by alpha, highest first — the leaderboard
    /// query the institutions ranking sorts on. Caller materialises and pages.
    /// </summary>
    public IQueryable<FundScore> GetRankedByAlpha(int windowYears, string benchmarkTicker)
    {
        return GetAll()
            .Where(s =>
                s.WindowYears == windowYears
                && s.BenchmarkTicker == benchmarkTicker
                && s.CalculationVersion == FundScore.CurrentCalculationVersion
            )
            .OrderByDescending(s => s.AlphaPercent);
    }
}
