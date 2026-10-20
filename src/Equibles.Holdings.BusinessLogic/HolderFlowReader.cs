using System.Globalization;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.BusinessLogic.Advisers;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// Reads market-wide holder flows: each closed quarter's filer trades from the intelligence
/// metrics, bucketed by a holder-mix dimension and optionally split by sector. A quarter's
/// inputs are cached per computation, so a recompute is read fresh.
/// </summary>
[Service]
public class HolderFlowReader
{
    public const int DefaultHistoryPeriods = 8;
    public const int MaxHistoryPeriods = 12;

    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly SectorRepository _sectorRepository;
    private readonly IMemoryCache _memoryCache;

    public HolderFlowReader(
        InstitutionalHolderRepository holderRepository,
        SectorRepository sectorRepository,
        IMemoryCache memoryCache
    )
    {
        _holderRepository = holderRepository;
        _sectorRepository = sectorRepository;
        _memoryCache = memoryCache;
    }

    /// <summary>Every computed closed quarter, oldest first.</summary>
    public async Task<List<HolderFlowQuarterState>> GetQuarters(
        CancellationToken cancellationToken = default
    ) => (
            await _holderRepository
                .GetComputedIntelligenceQuarters()
                .AsNoTracking()
                .OrderBy(state => state.ReportDate)
                .Select(state => new
                {
                    state.ReportDate,
                    state.ComputedAt,
                    state.CalculationVersion,
                })
                .ToListAsync(cancellationToken)
        ).Select(state => new HolderFlowQuarterState(state.ReportDate, state.ComputedAt.GetValueOrDefault(), state.CalculationVersion)).ToList();

    /// <summary>
    /// One quarter's flows by the dimension's buckets; the sector split is empty until the
    /// quarter carries sector rows (<see cref="HolderFlowQuarterState.SectorSplitAvailable"/>).
    /// </summary>
    public async Task<HolderFlowQuarter> ReadQuarter(
        HolderFlowQuarterState quarter,
        HolderMixDimension dimension,
        HolderFlowGrouping grouping = HolderFlowGrouping.None,
        CancellationToken cancellationToken = default
    )
    {
        var holders = await Holders(quarter, cancellationToken);
        if (grouping != HolderFlowGrouping.Sector || !quarter.SectorSplitAvailable)
            return HolderFlowCalculator.Build(quarter, dimension, grouping, holders);
        return HolderFlowCalculator.Build(
            quarter,
            dimension,
            grouping,
            holders,
            await SectorRows(quarter, cancellationToken),
            await SectorNames(quarter, cancellationToken)
        );
    }

    /// <summary>
    /// The last <paramref name="periods"/> computed quarters up to <paramref name="through"/>,
    /// oldest first, without a sector split.
    /// </summary>
    public async Task<List<HolderFlowQuarter>> ReadHistory(
        IReadOnlyList<HolderFlowQuarterState> quarters,
        DateOnly through,
        HolderMixDimension dimension,
        int periods = DefaultHistoryPeriods,
        CancellationToken cancellationToken = default
    )
    {
        var selected = quarters
            .Where(quarter => quarter.ReportDate <= through)
            .OrderBy(quarter => quarter.ReportDate)
            .TakeLast(Math.Clamp(periods, 1, MaxHistoryPeriods))
            .ToList();
        var history = new List<HolderFlowQuarter>(selected.Count);
        foreach (var quarter in selected)
            history.Add(
                await ReadQuarter(quarter, dimension, HolderFlowGrouping.None, cancellationToken)
            );
        return history;
    }

    private Task<List<HolderFlowHolder>> Holders(
        HolderFlowQuarterState quarter,
        CancellationToken cancellationToken
    ) =>
        _memoryCache.GetOrCreateAsync(
            CacheKey("holders", quarter),
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return await LoadHolders(quarter.ReportDate, cancellationToken);
            }
        );

    private Task<ILookup<Guid, HolderFlowSectorRow>> SectorRows(
        HolderFlowQuarterState quarter,
        CancellationToken cancellationToken
    ) =>
        _memoryCache.GetOrCreateAsync(
            CacheKey("sectors", quarter),
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                var rows = await _holderRepository
                    .GetSectorFlows(quarter.ReportDate)
                    .AsNoTracking()
                    .Select(flow => new HolderFlowSectorRow(
                        flow.InstitutionalHolderId,
                        flow.SectorGroup,
                        flow.SectorId,
                        flow.BuyValue,
                        flow.SellValue
                    ))
                    .ToListAsync(cancellationToken);
                return rows.ToLookup(row => row.InstitutionalHolderId);
            }
        );

    // Keyed like the rows, so names read after the quarter was computed cover every sector it uses.
    private Task<Dictionary<Guid, string>> SectorNames(
        HolderFlowQuarterState quarter,
        CancellationToken cancellationToken
    ) =>
        _memoryCache.GetOrCreateAsync(
            CacheKey("sector-names", quarter),
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return await _sectorRepository
                    .GetAll()
                    .AsNoTracking()
                    .ToDictionaryAsync(
                        sector => sector.Id,
                        sector => sector.Name,
                        cancellationToken
                    );
            }
        );

    // The quarter's measurements with each filer's profile as of the quarter end (current entity
    // type, client base and private funds from the Form ADV roster current then, as the holder
    // mix reads them) and filed address code.
    private async Task<List<HolderFlowHolder>> LoadHolders(
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        var profileRows = await _holderRepository
            .GetProfilesMeasuredIn(reportDate)
            .AsNoTracking()
            .Select(profile => new
            {
                profile.InstitutionalHolderId,
                profile.EntityType,
                profile.AdviserCrd,
            })
            .ToListAsync(cancellationToken);
        var advisers = await AdviserHistory.FactsAt(
            _holderRepository,
            AdviserHistory.Crds(profileRows.Select(profile => profile.AdviserCrd)),
            [reportDate],
            cancellationToken
        );
        var profiles = profileRows.ToDictionary(
            profile => profile.InstitutionalHolderId,
            profile =>
                HolderMixProfile.At(
                    profile.EntityType,
                    profile.AdviserCrd is { } crd
                        ? advisers.GetValueOrDefault((crd, reportDate))
                        : null
                )
        );
        var metrics = await _holderRepository
            .GetQuarterlyMetrics(reportDate)
            .AsNoTracking()
            .Select(row => new
            {
                row.InstitutionalHolderId,
                row.EquityAum,
                row.PreviousEquityAum,
                row.BuyValue,
                row.SellValue,
                row.UnpricedTradeCount,
                row.StyleBand,
                row.TurnoverBand,
                row.HorizonBand,
                row.InstitutionalHolder.StateOrCountry,
            })
            .ToListAsync(cancellationToken);
        return metrics
            .Select(row => new HolderFlowHolder
            {
                InstitutionalHolderId = row.InstitutionalHolderId,
                Mix = HolderMixHolder.Of(
                    profiles.GetValueOrDefault(row.InstitutionalHolderId),
                    row.StyleBand,
                    row.TurnoverBand,
                    row.HorizonBand,
                    row.EquityAum,
                    row.StateOrCountry
                ),
                EquityAum = row.EquityAum,
                PreviousEquityAum = row.PreviousEquityAum,
                BuyValue = row.BuyValue,
                SellValue = row.SellValue,
                UnpricedTradeCount = row.UnpricedTradeCount,
            })
            .ToList();
    }

    // The computation stamp keys the entry, so a recomputed quarter is never served stale.
    private static string CacheKey(string kind, HolderFlowQuarterState quarter) =>
        string.Join(
            ':',
            "holdings",
            "holder-flows",
            kind,
            quarter.ReportDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            quarter.ComputedAt.Ticks.ToString(CultureInfo.InvariantCulture)
        );
}
