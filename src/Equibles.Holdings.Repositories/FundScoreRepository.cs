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
            );
    }

    /// <summary>All current scores for one filer, latest window/benchmark variants included.</summary>
    public IQueryable<FundScore> GetByHolder(InstitutionalHolder holder)
    {
        return GetAll().Where(s => s.InstitutionalHolderId == holder.Id);
    }

    /// <summary>
    /// Scores for a given window/benchmark ranked by alpha, highest first — the leaderboard
    /// query the institutions ranking sorts on. Caller materialises and pages.
    /// </summary>
    public IQueryable<FundScore> GetRankedByAlpha(int windowYears, string benchmarkTicker)
    {
        return GetAll()
            .Where(s => s.WindowYears == windowYears && s.BenchmarkTicker == benchmarkTicker)
            .OrderByDescending(s => s.AlphaPercent);
    }
}
