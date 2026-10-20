using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services.Benchmarks;
using Equibles.Holdings.Repositories;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Rebuilds one closed quarter's <see cref="HolderQuarterlyMetrics"/>,
/// <see cref="HolderQuarterlySectorFlow"/> and <see cref="HolderQuarterlyBenchmarkActiveShare"/>
/// rows from Form 13F rows, a few hundred filers at a time, and keeps each filer's
/// <see cref="HolderStockPresence"/> bit for the quarter current. A changed presence bit re-dirties
/// every later quarter, whose tenure reads it.
/// </summary>
[Service]
public class HolderIntelligenceRefreshService
{
    /// <summary>
    /// Bump when the metrics change meaning, so the drain recomputes every quarter. 2: trades are
    /// priced at the quarter average price. 3: N-CEN index series assets feed the index-like band.
    /// 4: trades are also stored per sector (<see cref="HolderSectorFlowCalculator.FirstVersion"/>).
    /// 5: a trade whose shares, at its price, grossly exceed a position's reported value is unpriced
    /// (<see cref="HolderTurnoverCalculator"/>), which heals buy, sell, turnover, style bands and
    /// sector flows built on swapped share and value columns or a sibling class's CUSIP.
    /// 6: active share is also measured against every published index benchmark
    /// (<see cref="IHolderBenchmarkProvider"/>) and the closest one feeds the index-like band, sector
    /// active share is stored, and a filer that alone continues one predecessor's 13F reports trades
    /// against that predecessor's book (<see cref="HolderQuarterlyMetrics.ComparedHolderId"/>), which
    /// heals small- and mid-cap trackers read as active and successors' first quarters with no trades.
    /// </summary>
    public const int CalculationVersion = 6;

    /// <summary>A stamp this old is past the drain's cooldown and is picked up on its next tick.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(1);

    internal const int HolderBatchSize = 250;

    // Rows saved per round trip on publish, so the change tracker never holds a whole quarter.
    internal const int PublishBatchSize = 2_000;

    // A quarter-wide aggregate over millions of holdings outlasts the default 30-second timeout.
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(30);

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly HoldingsCorpusCoverage _corpusCoverage;
    private readonly IEnumerable<IHolderBenchmarkProvider> _benchmarkProviders;
    private readonly decimal _minimumBenchmarkCoverage;
    private readonly ILogger<HolderIntelligenceRefreshService> _logger;

    public HolderIntelligenceRefreshService(
        EquiblesFinancialDbContext dbContext,
        HoldingsCorpusCoverage corpusCoverage,
        IEnumerable<IHolderBenchmarkProvider> benchmarkProviders,
        IOptions<HolderBenchmarkOptions> benchmarkOptions,
        ILogger<HolderIntelligenceRefreshService> logger
    )
    {
        _dbContext = dbContext;
        _corpusCoverage = corpusCoverage;
        _benchmarkProviders = benchmarkProviders;
        _minimumBenchmarkCoverage = Math.Clamp(benchmarkOptions.Value.MinimumCoverage, 0, 1);
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
        _dbContext.Database.SetCommandTimeout(CommandTimeout);
        var quarter = HolderTenureCalculator.QuarterIndex(reportDate).Value;
        var bit = HolderTenureCalculator.Set(0, quarter);
        var previous = HoldingsCorpusCoverage.PreviousQuarterEnd(reportDate);
        var priorDates = new[] { 3, 2, 1 }
            .Select(back => reportDate.AddDays(1).AddMonths(-3 * back).AddDays(-1))
            .ToList();

        var benchmarks = await LoadBenchmarks(reportDate, cancellationToken);
        var filers = await LoadFilers(reportDate, cancellationToken);
        // A quarter before the corpus is incidental, so the first covered quarter has no trades.
        var previousFilers =
            previous >= FirstComputableQuarter
                ? (await LoadFilers(previous, cancellationToken)).ToHashSet()
                : [];

        // Only a quarter with a baseline has trades to price.
        var averagePrices =
            previousFilers.Count > 0
                ? await QuarterAveragePriceLoader.Load(
                    _dbContext,
                    reportDate,
                    previous,
                    today,
                    cancellationToken
                )
                : [];
        var advisedSeries = HolderIdentifierRules.SummarizeAdvisedSeries(
            await AdvisedSeriesLoader.Load(_dbContext, reportDate, cancellationToken)
        );
        var (issuerSectors, exchangeTraded) = await LoadSectors(cancellationToken);
        var aggregateSectorWeights = benchmarks.FirstOrDefault(benchmark =>
            benchmark.Key == HolderBenchmarkCatalog.Aggregate
        )
            is { } aggregate
            ? HolderSectorWeights.Of(aggregate.Weights, issuerSectors)
            : [];
        var predecessors =
            previousFilers.Count > 0
                ? await LoadComparedPredecessors(
                    reportDate,
                    previous,
                    previousFilers,
                    cancellationToken
                )
                : [];

        var presenceChanged = await ClearNonFilers(reportDate, bit, cancellationToken);

        var metrics = new List<HolderQuarterlyMetrics>(filers.Count);
        var sectorFlows = new List<HolderQuarterlySectorFlow>();
        var benchmarkRows = new List<HolderQuarterlyBenchmarkActiveShare>();
        foreach (var batch in filers.Chunk(HolderBatchSize))
        {
            var (results, presenceIds) = await BuildBatch(
                batch,
                reportDate,
                previous,
                previousFilers,
                predecessors,
                priorDates,
                benchmarks,
                aggregateSectorWeights,
                averagePrices,
                advisedSeries,
                issuerSectors,
                exchangeTraded,
                cancellationToken
            );
            metrics.AddRange(results.Select(result => result.Result.Metrics));
            sectorFlows.AddRange(results.SelectMany(result => result.Result.SectorFlows));
            benchmarkRows.AddRange(
                results.SelectMany(result => result.Result.BenchmarkActiveShares)
            );
            presenceChanged |= await WritePresence(
                results,
                presenceIds,
                reportDate,
                bit,
                cancellationToken
            );
        }

        await BeforePublish(reportDate);
        await Publish(reportDate, metrics, sectorFlows, benchmarkRows, cancellationToken);

        _logger.LogInformation(
            "Holder intelligence for {ReportDate}: {Count} filers, {SectorFlows} sector flow rows, {Benchmarks} benchmarks ({BenchmarkKeys}), {Priced} securities at their quarter average price, {Advised} filers with N-CEN series, {Compared} compared with a predecessor, presence changed: {Changed}",
            reportDate,
            metrics.Count,
            sectorFlows.Count,
            benchmarks.Count,
            string.Join(", ", benchmarks.Select(benchmark => benchmark.Key)),
            averagePrices.Count,
            metrics.Count(row => row.AdvisedSeriesAsOf != null),
            metrics.Count(row =>
                row.ComparedHolderId != null && row.ComparedHolderId != row.InstitutionalHolderId
            ),
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
        Dictionary<Guid, Guid> predecessors,
        List<DateOnly> priorDates,
        List<HolderBenchmark> benchmarks,
        Dictionary<Guid, decimal> aggregateSectorWeights,
        Dictionary<HolderSecurityKey, decimal> averagePrices,
        Dictionary<int, AdvisedSeriesSummary> advisedSeries,
        Dictionary<Guid, Guid> issuerSectors,
        HashSet<HolderSecurityKey> exchangeTraded,
        CancellationToken cancellationToken
    )
    {
        // Nothing before the corpus is a baseline, so its rows are never read.
        var readPrevious = previousFilers.Count > 0;
        // A filer that alone continues a predecessor compares with that predecessor's last book.
        var comparedIds = holderIds.ToDictionary(
            holderId => holderId,
            holderId =>
                previousFilers.Contains(holderId) ? holderId
                : predecessors.TryGetValue(holderId, out var predecessor) ? predecessor
                : (Guid?)null
        );
        var predecessorIds = comparedIds
            .Where(pair => pair.Value is { } compared && compared != pair.Key)
            .Select(pair => pair.Value.Value)
            .ToList();
        var rows = await _dbContext
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .Where(holding =>
                (
                    holderIds.Contains(holding.InstitutionalHolderId)
                        && (
                            holding.ReportDate == reportDate
                            || readPrevious && holding.ReportDate == previous
                        )
                    || predecessorIds.Contains(holding.InstitutionalHolderId)
                        && holding.ReportDate == previous
                )
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
                FiledValue = holding.FiledValue,
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
        var adviserCrds = await AdviserCrds(holderIds, cancellationToken);

        var rowsByHolder = rows.ToLookup(row => row.InstitutionalHolderId);
        var presenceByHolder = presence.ToLookup(row => row.InstitutionalHolderId);
        var filedByHolder = filed.ToLookup(row => row.InstitutionalHolderId);
        var turnoverByHolder = turnovers.ToLookup(row => row.InstitutionalHolderId);
        var results = holderIds
            .Select(holderId =>
            {
                var holderRows = rowsByHolder[holderId].ToList();
                var series = adviserCrds.TryGetValue(holderId, out var crd)
                    ? advisedSeries.GetValueOrDefault(crd)
                    : null;
                var comparedId = comparedIds[holderId];
                var input = new HolderQuarterInput
                {
                    InstitutionalHolderId = holderId,
                    ReportDate = reportDate,
                    Current = holderRows.Where(row => row.ReportDate == reportDate).ToList(),
                    Previous = comparedId is { } compared
                        ? rowsByHolder[compared].Where(row => row.ReportDate == previous).ToList()
                        : null,
                    ComparedHolderId = comparedId,
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
                    Benchmarks = benchmarks,
                    MinimumBenchmarkCoverage = _minimumBenchmarkCoverage,
                    AggregateSectorWeights = aggregateSectorWeights,
                    AveragePrices = averagePrices,
                    PriorTurnovers = priorDates
                        .Select(date =>
                            turnoverByHolder[holderId]
                                .FirstOrDefault(row => row.ReportDate == date)
                                ?.Turnover
                        )
                        .ToList(),
                    IndexSeriesNetAssets = series?.IndexNetAssets,
                    AdvisedSeriesNetAssets = series?.NetAssets,
                    AdvisedSeriesAsOf = series?.AsOf,
                    IssuerSectors = issuerSectors,
                    ExchangeTradedSecurities = exchangeTraded,
                };
                return (holderId, HolderQuarterMetricsBuilder.Build(input, CalculationVersion));
            })
            .ToList();
        return (results, presence.ToDictionary(row => row.Key, row => row.Id));
    }

    // The adviser whose N-CEN series describe each filer: the Form ADV adviser behind its profile,
    // else the CRD on its own 13F cover, as the profile's advised series are read.
    private async Task<Dictionary<Guid, int>> AdviserCrds(
        Guid[] holderIds,
        CancellationToken cancellationToken
    )
    {
        var profiles = (
            await _dbContext
                .Set<InstitutionalHolderProfile>()
                .Where(profile =>
                    holderIds.Contains(profile.InstitutionalHolderId) && profile.AdviserCrd != null
                )
                .Select(profile => new { profile.InstitutionalHolderId, profile.AdviserCrd })
                .ToListAsync(cancellationToken)
        ).ToDictionary(
            profile => profile.InstitutionalHolderId,
            profile => profile.AdviserCrd.Value
        );
        var covers = await _dbContext
            .Set<InstitutionalHolder>()
            .AsNoTracking()
            .Where(holder => holderIds.Contains(holder.Id) && holder.CrdNumber != null)
            .Select(holder => new { holder.Id, holder.CrdNumber })
            .ToListAsync(cancellationToken);
        var coverCrds = covers
            .Select(cover => (cover.Id, Crd: HolderIdentifierRules.ParseCrd(cover.CrdNumber)))
            .Where(cover => cover.Crd != null);
        foreach (var (holderId, crd) in coverCrds)
            profiles.TryAdd(holderId, crd.GetValueOrDefault());
        return profiles;
    }

    private Task<List<PresenceRow>> LoadPresence(
        IReadOnlyCollection<Guid> holderIds,
        CancellationToken cancellationToken
    ) =>
        _dbContext
            .Set<HolderStockPresence>()
            .Where(row => holderIds.Contains(row.InstitutionalHolderId))
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
    // rebuild of another quarter never loses its own bit, and stamps later quarters with them.
    private async Task<bool> WritePresence(
        List<(Guid HolderId, HolderQuarterResult Result)> results,
        Dictionary<PresenceKey, Guid> presenceIds,
        DateOnly reportDate,
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
            foreach (var row in await LoadPresence(holderIds, cancellationToken))
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
        await DirtyLaterQuarters(reportDate, null, cancellationToken);
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

    // Stamps later quarters, up to through when given, with a fresh time already past the
    // cooldown, so the drain picks them up next and a rebuild already reading them re-runs.
    private Task DirtyLaterQuarters(
        DateOnly reportDate,
        DateOnly? through,
        CancellationToken cancellationToken
    )
    {
        var stamp = DateTime.UtcNow - Cooldown - TimeSpan.FromSeconds(1);
        var last = through ?? DateOnly.MaxValue;
        return _dbContext
            .Set<HolderIntelligenceQuarterState>()
            .Where(state => state.ReportDate > reportDate && state.ReportDate <= last)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(state => state.DirtyAt, stamp),
                cancellationToken
            );
    }

    // A filer whose 13F for the quarter is gone no longer holds anything in it; the clear and
    // the stamp of the later quarters whose tenure reads it commit together.
    private async Task<bool> ClearNonFilers(
        DateOnly reportDate,
        long bit,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await BeginTransaction(cancellationToken);
        var cleared = await _dbContext
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
            );
        if (cleared > 0)
            await DirtyLaterQuarters(reportDate, null, cancellationToken);
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);
        return cleared > 0;
    }

    // Replaces the quarter's metrics, sector flows and benchmark active shares in one transaction,
    // saved a batch at a time. A changed filer set re-dirties every later quarter (tenure bridges
    // unfiled quarters); a changed turnover the three whose trailing sum reads it.
    private async Task Publish(
        DateOnly reportDate,
        List<HolderQuarterlyMetrics> metrics,
        List<HolderQuarterlySectorFlow> sectorFlows,
        List<HolderQuarterlyBenchmarkActiveShare> benchmarkRows,
        CancellationToken cancellationToken
    )
    {
        await using (var transaction = await BeginTransaction(cancellationToken))
        {
            var stored = await _dbContext
                .Set<HolderQuarterlyMetrics>()
                .Where(row => row.ReportDate == reportDate)
                .Select(row => new { row.InstitutionalHolderId, row.Turnover })
                .ToDictionaryAsync(
                    row => row.InstitutionalHolderId,
                    row => row.Turnover,
                    cancellationToken
                );
            await _dbContext
                .Set<HolderQuarterlyMetrics>()
                .Where(row => row.ReportDate == reportDate)
                .ExecuteDeleteAsync(cancellationToken);
            await _dbContext
                .Set<HolderQuarterlySectorFlow>()
                .Where(row => row.ReportDate == reportDate)
                .ExecuteDeleteAsync(cancellationToken);
            await _dbContext
                .Set<HolderQuarterlyBenchmarkActiveShare>()
                .Where(row => row.ReportDate == reportDate)
                .ExecuteDeleteAsync(cancellationToken);
            await AddInBatches(metrics, cancellationToken);
            await AddInBatches(sectorFlows, cancellationToken);
            await AddInBatches(benchmarkRows, cancellationToken);

            var filersChanged =
                stored.Count != metrics.Count
                || metrics.Any(row => !stored.ContainsKey(row.InstitutionalHolderId));
            if (filersChanged)
                await DirtyLaterQuarters(reportDate, null, cancellationToken);
            else if (
                metrics.Any(row => TurnoverChanged(stored[row.InstitutionalHolderId], row.Turnover))
            )
                await DirtyLaterQuarters(
                    reportDate,
                    reportDate.AddDays(1).AddMonths(3 * TrailingQuarters).AddDays(-1),
                    cancellationToken
                );
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }
        _dbContext.ChangeTracker.Clear();
    }

    private async Task AddInBatches<T>(List<T> rows, CancellationToken cancellationToken)
        where T : class
    {
        foreach (var batch in rows.Chunk(PublishBatchSize))
        {
            _dbContext.Set<T>().AddRange(batch);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
        }
    }

    // Postgres rounds the turnover half away from zero to the column's six decimals.
    internal static bool TurnoverChanged(decimal? stored, decimal? current) =>
        stored
        != (current == null ? null : Math.Round(current.Value, 6, MidpointRounding.AwayFromZero));

    // The quarters after this one whose trailing annual turnover sums its turnover.
    private const int TrailingQuarters = 3;

    // Test seam for a failure after the presence bits are written and before the metrics publish.
    protected virtual Task BeforePublish(DateOnly reportDate) => Task.CompletedTask;

    // Every provider's benchmarks for the quarter, one per catalog key; a provider that throws
    // fails the rebuild, so a quarter never publishes against a partial benchmark set.
    private async Task<List<HolderBenchmark>> LoadBenchmarks(
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        var candidates = new List<HolderBenchmark>();
        foreach (var provider in _benchmarkProviders)
            candidates.AddRange(await provider.Load(reportDate, cancellationToken));
        foreach (var unknown in candidates.Where(c => !HolderBenchmarkCatalog.Contains(c.Key)))
            _logger.LogWarning(
                "Holder benchmark {Key} from {Source} is not in the catalog and is ignored",
                unknown.Key,
                unknown.Source
            );
        var chosen = HolderBenchmarkSelection.Choose(candidates, reportDate);
        foreach (var thin in chosen.Where(c => c.Coverage < _minimumBenchmarkCoverage))
            _logger.LogWarning(
                "Holder benchmark {Key} from {Source} resolves {Coverage:P1} of its weight to issuers, under the {Minimum:P0} floor; it is stored but never the closest benchmark for {ReportDate}",
                thin.Key,
                thin.Source,
                thin.Coverage,
                _minimumBenchmarkCoverage,
                reportDate
            );
        return chosen;
    }

    // Successor to predecessor for each comparable succession that pairs one filer with one, whose
    // successor first files this quarter and whose predecessor filed the quarter before. A group
    // of several filers cannot divide its prior book, so its successors keep no baseline.
    private async Task<Dictionary<Guid, Guid>> LoadComparedPredecessors(
        DateOnly reportDate,
        DateOnly previous,
        HashSet<Guid> previousFilers,
        CancellationToken cancellationToken
    )
    {
        var pairs = await _dbContext
            .Set<HolderSuccession>()
            .AsNoTracking()
            .Where(succession =>
                succession.SuccessorFirstReportDate == reportDate
                && succession.PredecessorLastReportDate == previous
            )
            .Select(succession => new HoldingsSuccessionPair
            {
                PredecessorHolderId = succession.PredecessorHolderId,
                SuccessorHolderId = succession.SuccessorHolderId,
                SuccessorFirstReportDate = succession.SuccessorFirstReportDate,
                PredecessorGroupValue = succession.PredecessorGroupValue,
                SuccessorGroupValue = succession.SuccessorGroupValue,
            })
            .ToListAsync(cancellationToken);
        return HoldingsSuccessionGroups
            .Build(pairs)
            .Where(group =>
                group.Comparable && group.Predecessors.Count == 1 && group.Successors.Count == 1
            )
            .Select(group =>
                (
                    Successor: group.Successors.Keys.Single(),
                    Predecessor: group.Predecessors.Keys.Single()
                )
            )
            .Where(pair =>
                !previousFilers.Contains(pair.Successor)
                && previousFilers.Contains(pair.Predecessor)
            )
            .ToDictionary(pair => pair.Successor, pair => pair.Predecessor);
    }

    // Each issuer's industry sector as of now, and the US listings flagged as exchange-traded
    // products by the reference directory: a presentation listing's rows carry no listed ticker.
    private async Task<(
        Dictionary<Guid, Guid> IssuerSectors,
        HashSet<HolderSecurityKey> ExchangeTraded
    )> LoadSectors(CancellationToken cancellationToken)
    {
        var sectors = await _dbContext
            .Set<EquityIssuer>()
            .AsNoTracking()
            .Where(issuer => issuer.Industry.SectorId != null)
            .Select(issuer => new { issuer.Id, issuer.Industry.SectorId })
            .ToDictionaryAsync(
                issuer => issuer.Id,
                issuer => issuer.SectorId.Value,
                cancellationToken
            );
        var listings = await _dbContext
            .Set<EquityListing>()
            .AsNoTracking()
            .Where(listing => listing.MarketCountryCode == "US" && listing.IsReferenceListed)
            .Select(listing => new
            {
                listing.Security.EquityIssuerId,
                listing.Ticker,
                IsPresentation = listing.Security.Issuer.Presentation.EquityListingId == listing.Id,
            })
            .ToListAsync(cancellationToken);
        var exchangeTraded = new HashSet<HolderSecurityKey>();
        foreach (var listing in listings)
        {
            exchangeTraded.Add(new HolderSecurityKey(listing.EquityIssuerId, listing.Ticker));
            if (listing.IsPresentation)
                exchangeTraded.Add(new HolderSecurityKey(listing.EquityIssuerId, null));
        }
        return (sectors, exchangeTraded);
    }

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
