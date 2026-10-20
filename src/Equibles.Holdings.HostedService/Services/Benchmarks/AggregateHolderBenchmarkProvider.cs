using Equibles.Data;
using Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services.Benchmarks;

/// <summary>
/// Every 13F filer's combined book: the valued Form 13F common-share positions per issuer at the
/// quarter end, the benchmark behind <see cref="HolderQuarterlyMetrics.AggregateActiveShare"/>.
/// </summary>
public class AggregateHolderBenchmarkProvider : IHolderBenchmarkProvider
{
    private readonly EquiblesFinancialDbContext _dbContext;

    public AggregateHolderBenchmarkProvider(EquiblesFinancialDbContext dbContext) =>
        _dbContext = dbContext;

    public async Task<IReadOnlyList<HolderBenchmark>> Load(
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        // A quarter-wide sum over millions of holdings outlasts the default command timeout.
        _dbContext.Database.SetCommandTimeout(HolderIntelligenceRefreshService.CommandTimeout);
        var weights = await _dbContext
            .Set<InstitutionalHolding>()
            .Where(holding =>
                holding.ReportDate == reportDate
                && holding.FilingType == FilingType.Form13F
                && holding.ShareType == ShareType.Shares
                && holding.OptionType == null
                && !holding.ValuePending
                && !holding.ValueUnavailable
                && holding.Value > 0
            )
            .GroupBy(holding => holding.EquityIssuerId)
            .Select(group => new { IssuerId = group.Key, Value = group.Sum(h => h.Value) })
            .ToDictionaryAsync(row => row.IssuerId, row => (decimal)row.Value, cancellationToken);
        return weights.Count == 0
            ? []
            :
            [
                new HolderBenchmark
                {
                    Key = HolderBenchmarkCatalog.Aggregate,
                    AsOf = reportDate,
                    Weights = weights,
                    Source = "Form 13F combined book",
                },
            ];
    }
}
