using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Rebuilds one closed quarter's <see cref="HolderQuarterlyMetrics"/> from Form 13F rows, a few
/// hundred filers at a time, and keeps each filer's <see cref="HolderStockPresence"/> bit for the
/// quarter current. A changed presence bit re-dirties every later quarter, whose tenure reads it.
/// </summary>
[Service]
public class HolderIntelligenceRefreshService
{
    /// <summary>Bump when the metrics change meaning, so the drain recomputes every quarter.</summary>
    public const int CalculationVersion = 1;

    /// <summary>A stamp this old is past the drain's cooldown and is picked up on its next tick.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(1);

    internal const int HolderBatchSize = 250;

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly HoldingsCorpusCoverage _corpusCoverage;
    private readonly ILogger<HolderIntelligenceRefreshService> _logger;

    public HolderIntelligenceRefreshService(
        EquiblesFinancialDbContext dbContext,
        HoldingsCorpusCoverage corpusCoverage,
        ILogger<HolderIntelligenceRefreshService> logger
    )
    {
        _dbContext = dbContext;
        _corpusCoverage = corpusCoverage;
        _logger = logger;
    }

    /// <summary>
    /// The 13F corpus's first complete quarter, never before the first bit of the presence masks;
    /// tenure reaching it is censored.
    /// </summary>
    public DateOnly FirstComputableQuarter =>
        _corpusCoverage.CoverageStartDate > HolderTenureCalculator.FirstQuarter
            ? _corpusCoverage.CoverageStartDate
            : HolderTenureCalculator.FirstQuarter;

    /// <summary>A covered quarter end whose 13F filing window has closed.</summary>
    public bool IsComputable(DateOnly reportDate, DateOnly today) =>
        reportDate >= FirstComputableQuarter
        && HolderTenureCalculator.QuarterIndex(reportDate) != null
        && reportDate <= CombinedQuarterHelper.CompletedReportDateCutoff(today);

    /// <summary>
    /// Replaces the quarter's metrics in one transaction. Returns false, writing nothing, for a
    /// quarter that is not computable.
    /// </summary>
    public async Task<bool> RebuildQuarter(
        DateOnly reportDate,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        if (!IsComputable(reportDate, today))
            return false;
        var quarter = HolderTenureCalculator.QuarterIndex(reportDate).Value;
        var bit = HolderTenureCalculator.Set(0, quarter);
        var previous = HoldingsCorpusCoverage.PreviousQuarterEnd(reportDate);
        var priorDates = new[] { 3, 2, 1 }
            .Select(back => reportDate.AddDays(1).AddMonths(-3 * back).AddDays(-1))
            .ToList();

        var benchmark = await LoadAggregateBenchmark(reportDate, cancellationToken);
        var filers = await LoadFilers(reportDate, cancellationToken);
        // A quarter before the corpus is incidental, so the first covered quarter has no trades.
        var previousFilers =
            previous >= FirstComputableQuarter
                ? (await LoadFilers(previous, cancellationToken)).ToHashSet()
                : [];

        // A filer whose 13F for the quarter is gone no longer holds anything in it.
        var presenceChanged =
            await _dbContext
                .Set<HolderStockPresence>()
                .Where(presence =>
                    (presence.HeldMask & bit) != 0
                    && !_dbContext
                        .Set<InstitutionalFiling>()
                        .Any(filing =>
                            filing.InstitutionalHolderId == presence.InstitutionalHolderId
                            && filing.ReportDate == reportDate
                            && filing.FilingType == FilingType.Form13F
                        )
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(p => p.HeldMask, p => p.HeldMask & ~bit),
                    cancellationToken
                ) > 0;

        var metrics = new List<HolderQuarterlyMetrics>(filers.Count);
        foreach (var batch in filers.Chunk(HolderBatchSize))
        {
            var (results, presenceIds) = await BuildBatch(
                batch,
                reportDate,
                previous,
                previousFilers,
                priorDates,
                benchmark,
                cancellationToken
            );
            metrics.AddRange(results.Select(result => result.Result.Metrics));
            presenceChanged |= await WritePresence(results, presenceIds, bit, cancellationToken);
        }

        await using (var transaction = await BeginTransaction(cancellationToken))
        {
            await _dbContext
                .Set<HolderQuarterlyMetrics>()
                .Where(row => row.ReportDate == reportDate)
                .ExecuteDeleteAsync(cancellationToken);
            _dbContext.Set<HolderQuarterlyMetrics>().AddRange(metrics);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (presenceChanged)
                await DirtyLaterQuarters(reportDate, cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }
        _dbContext.ChangeTracker.Clear();

        _logger.LogInformation(
            "Holder intelligence for {ReportDate}: {Count} filers, presence changed: {Changed}",
            reportDate,
            metrics.Count,
            presenceChanged
        );
        return true;
    }

    private async Task<(
        List<(Guid HolderId, HolderQuarterResult Result)> Results,
        Dictionary<PresenceKey, Guid> PresenceIds
    )> BuildBatch(
        Guid[] holderIds,
        DateOnly reportDate,
        DateOnly previous,
        HashSet<Guid> previousFilers,
        List<DateOnly> priorDates,
        Dictionary<Guid, decimal> benchmark,
        CancellationToken cancellationToken
    )
    {
        var rows = await _dbContext
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .Where(holding =>
                holderIds.Contains(holding.InstitutionalHolderId)
                && (holding.ReportDate == reportDate || holding.ReportDate == previous)
                && holding.FilingType == FilingType.Form13F
            )
            .Select(holding => new HolderIntelligenceRow
            {
                InstitutionalHolderId = holding.InstitutionalHolderId,
                EquityIssuerId = holding.EquityIssuerId,
                ListedTicker = holding.ListedTicker,
                ReportDate = holding.ReportDate,
                ShareType = holding.ShareType,
                OptionType = holding.OptionType,
                Shares = holding.Shares,
                Value = holding.Value,
                ValueMissing = holding.ValuePending || holding.ValueUnavailable,
            })
            .ToListAsync(cancellationToken);
        var presence = await LoadPresence(holderIds, cancellationToken);
        var firstQuarter = FirstComputableQuarter;
        var filed = await _dbContext
            .Set<InstitutionalFiling>()
            .Where(filing =>
                holderIds.Contains(filing.InstitutionalHolderId)
                && filing.FilingType == FilingType.Form13F
                && filing.ReportDate >= firstQuarter
                && filing.ReportDate <= reportDate
            )
            .Select(filing => new { filing.InstitutionalHolderId, filing.ReportDate })
            .Distinct()
            .ToListAsync(cancellationToken);
        var turnovers = await _dbContext
            .Set<HolderQuarterlyMetrics>()
            .Where(row =>
                holderIds.Contains(row.InstitutionalHolderId) && priorDates.Contains(row.ReportDate)
            )
            .Select(row => new
            {
                row.InstitutionalHolderId,
                row.ReportDate,
                row.Turnover,
            })
            .ToListAsync(cancellationToken);
        var factors = await PreviousShareFactors(
            rows.Where(row => row.ReportDate == previous).ToList(),
            previous,
            reportDate,
            cancellationToken
        );

        var rowsByHolder = rows.ToLookup(row => row.InstitutionalHolderId);
        var presenceByHolder = presence.ToLookup(row => row.InstitutionalHolderId);
        var filedByHolder = filed.ToLookup(row => row.InstitutionalHolderId);
        var turnoverByHolder = turnovers.ToLookup(row => row.InstitutionalHolderId);
        var results = holderIds
            .Select(holderId =>
            {
                var holderRows = rowsByHolder[holderId].ToList();
                var input = new HolderQuarterInput
                {
                    InstitutionalHolderId = holderId,
                    ReportDate = reportDate,
                    Current = holderRows.Where(row => row.ReportDate == reportDate).ToList(),
                    Previous = previousFilers.Contains(holderId)
                        ? holderRows.Where(row => row.ReportDate == previous).ToList()
                        : null,
                    PreviousShareFactors = factors,
                    Presence = presenceByHolder[holderId]
                        .ToDictionary(row => row.Key.Security, row => row.HeldMask),
                    FiledMask = filedByHolder[holderId]
                        .Aggregate(
                            0L,
                            (mask, row) =>
                                HolderTenureCalculator.QuarterIndex(row.ReportDate) is { } index
                                    ? HolderTenureCalculator.Set(mask, index)
                                    : mask
                        ),
                    FirstCoveredQuarter = HolderTenureCalculator.QuarterIndex(firstQuarter).Value,
                    AggregateBenchmark = benchmark,
                    PriorTurnovers = priorDates
                        .Select(date =>
                            turnoverByHolder[holderId]
                                .FirstOrDefault(row => row.ReportDate == date)
                                ?.Turnover
                        )
                        .ToList(),
                };
                return (holderId, HolderQuarterMetricsBuilder.Build(input, CalculationVersion));
            })
            .ToList();
        return (results, presence.ToDictionary(row => row.Key, row => row.Id));
    }

    private Task<List<PresenceRow>> LoadPresence(
        IReadOnlyCollection<Guid> holderIds,
        CancellationToken cancellationToken,
        bool emptyOnly = false
    ) =>
        _dbContext
            .Set<HolderStockPresence>()
            .Where(row =>
                holderIds.Contains(row.InstitutionalHolderId) && (!emptyOnly || row.HeldMask == 0)
            )
            .Select(row => new PresenceRow
            {
                Id = row.Id,
                InstitutionalHolderId = row.InstitutionalHolderId,
                EquityIssuerId = row.EquityIssuerId,
                ListedTicker = row.ListedTicker,
                HeldMask = row.HeldMask,
            })
            .ToListAsync(cancellationToken);

    // The factor that puts each previous-quarter security's share count on the quarter's basis;
    // only securities with a split dated inside the quarter get an entry, null when unresolved.
    private async Task<Dictionary<HolderSecurityKey, decimal?>> PreviousShareFactors(
        List<HolderIntelligenceRow> previousRows,
        DateOnly previous,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        var issuerIds = previousRows.Select(row => row.EquityIssuerId).Distinct().ToList();
        var splits = await StockSplitQueries
            .ForIssuers(_dbContext, issuerIds)
            .AsNoTracking()
            .Where(split => split.EffectiveDate > previous && split.EffectiveDate <= reportDate)
            .ToListAsync(cancellationToken);
        if (splits.Count == 0)
            return [];
        var splitIssuers = splits.Select(split => split.EquityIssuerId).Distinct().ToList();
        var primaryTickers = await _dbContext
            .Set<EquityIssuer>()
            .Where(issuer => splitIssuers.Contains(issuer.Id))
            .Select(issuer => new { issuer.Id, issuer.Presentation.Listing.Ticker })
            .ToDictionaryAsync(issuer => issuer.Id, issuer => issuer.Ticker, cancellationToken);
        var splitsByIssuer = splits.ToLookup(split => split.EquityIssuerId);
        return previousRows
            .Select(row => row.Security)
            .Distinct()
            .Where(security => splitsByIssuer.Contains(security.IssuerId))
            .ToDictionary(
                security => security,
                security =>
                    Factor(
                        splitsByIssuer[security.IssuerId].ToList(),
                        primaryTickers.GetValueOrDefault(security.IssuerId),
                        security.ListedTicker,
                        previous
                    )
            );
    }

    private static decimal? Factor(
        List<StockSplit> splits,
        string primaryTicker,
        string listedTicker,
        DateOnly previous
    )
    {
        var ticker = listedTicker ?? primaryTicker;
        if (ticker == null || PriceSeriesSplitScope.HasUnresolvedBasis(splits, ticker, previous))
            return null;
        return SplitAdjustment.ShareCountFactor(
            previous,
            ShareCountSplitScope.ForListedSeries(splits, primaryTicker ?? ticker, ticker)
        );
    }

    // Sets and clears the batch's presence bits relative to the stored masks, so a concurrent
    // rebuild of another quarter never loses its own bit. Returns whether any bit changed.
    private async Task<bool> WritePresence(
        List<(Guid HolderId, HolderQuarterResult Result)> results,
        Dictionary<PresenceKey, Guid> presenceIds,
        long bit,
        CancellationToken cancellationToken
    )
    {
        var toSet = results
            .SelectMany(result =>
                result.Result.PresenceToSet.Select(security => new PresenceKey(
                    result.HolderId,
                    security
                ))
            )
            .ToList();
        var toClear = results
            .SelectMany(result =>
                result.Result.PresenceToClear.Select(security => new PresenceKey(
                    result.HolderId,
                    security
                ))
            )
            .ToList();
        if (toSet.Count == 0 && toClear.Count == 0)
            return false;

        await using var transaction = await BeginTransaction(cancellationToken);
        var missing = toSet.Where(key => !presenceIds.ContainsKey(key)).ToList();
        if (missing.Count > 0)
        {
            await InsertPresence(missing, cancellationToken);
            var holderIds = missing.Select(key => key.HolderId).Distinct().ToList();
            foreach (var row in await LoadPresence(holderIds, cancellationToken, emptyOnly: true))
                presenceIds[row.Key] = row.Id;
        }
        var setIds = toSet.Select(key => presenceIds[key]).ToList();
        var clearIds = toClear.Select(key => presenceIds[key]).ToList();
        await _dbContext
            .Set<HolderStockPresence>()
            .Where(row => setIds.Contains(row.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.HeldMask, row => row.HeldMask | bit),
                cancellationToken
            );
        await _dbContext
            .Set<HolderStockPresence>()
            .Where(row => clearIds.Contains(row.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.HeldMask, row => row.HeldMask & ~bit),
                cancellationToken
            );
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // New presence rows start empty; a row a concurrent rebuild inserted first is kept.
    private Task InsertPresence(List<PresenceKey> keys, CancellationToken cancellationToken) =>
        _dbContext
            .Set<HolderStockPresence>()
            .UpsertRange(
                keys.Select(key => new HolderStockPresence
                {
                    InstitutionalHolderId = key.HolderId,
                    EquityIssuerId = key.Security.IssuerId,
                    ListedTicker = key.Security.ListedTicker,
                })
            )
            .On(row => new
            {
                row.InstitutionalHolderId,
                row.EquityIssuerId,
                row.ListedTicker,
            })
            .NoUpdate()
            .RunAsync(cancellationToken);

    // A later quarter's tenure reads this quarter's bits: stamp it with a fresh time already past
    // the cooldown, so the drain picks it up next and a rebuild already reading it re-runs.
    private Task DirtyLaterQuarters(DateOnly reportDate, CancellationToken cancellationToken)
    {
        var stamp = DateTime.UtcNow - Cooldown - TimeSpan.FromSeconds(1);
        return _dbContext
            .Set<HolderIntelligenceQuarterState>()
            .Where(state => state.ReportDate > reportDate)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(state => state.DirtyAt, stamp),
                cancellationToken
            );
    }

    // Every 13F filer's common-share value per issuer, the benchmark aggregate active share uses.
    private Task<Dictionary<Guid, decimal>> LoadAggregateBenchmark(
        DateOnly reportDate,
        CancellationToken cancellationToken
    ) =>
        _dbContext
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

    // Filers with a Form 13F for the quarter, including a restatement that reports no positions.
    private Task<List<Guid>> LoadFilers(DateOnly reportDate, CancellationToken cancellationToken) =>
        _dbContext
            .Set<InstitutionalFiling>()
            .Where(filing =>
                filing.ReportDate == reportDate && filing.FilingType == FilingType.Form13F
            )
            .Select(filing => filing.InstitutionalHolderId)
            .Distinct()
            .OrderBy(holderId => holderId)
            .ToListAsync(cancellationToken);

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransaction(
        CancellationToken cancellationToken
    ) =>
        _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private readonly record struct PresenceKey(Guid HolderId, HolderSecurityKey Security);

    private sealed class PresenceRow
    {
        public Guid Id { get; init; }
        public Guid InstitutionalHolderId { get; init; }
        public Guid EquityIssuerId { get; init; }
        public string ListedTicker { get; init; }
        public long HeldMask { get; init; }

        public PresenceKey Key =>
            new(InstitutionalHolderId, new HolderSecurityKey(EquityIssuerId, ListedTicker));
    }
}
