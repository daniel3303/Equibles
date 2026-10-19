using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Sums one quarter's filer trades into the buckets of a holder-mix dimension, bucketing each
/// filer as the holder mix does from its profile and that quarter's measurements.
/// </summary>
public static class HolderFlowCalculator
{
    public const string TotalToken = "all";
    public const string TotalLabel = "All 13F filers";

    /// <param name="sectorRows">Each filer's sector rows; read only for the sector grouping.</param>
    /// <param name="sectorNames">The name of every sector id the rows carry.</param>
    public static HolderFlowQuarter Build(
        HolderFlowQuarterState state,
        HolderMixDimension dimension,
        HolderFlowGrouping grouping,
        IReadOnlyList<HolderFlowHolder> holders,
        ILookup<Guid, HolderFlowSectorRow> sectorRows = null,
        IReadOnlyDictionary<Guid, string> sectorNames = null
    )
    {
        var bySector = grouping == HolderFlowGrouping.Sector && state.SectorSplitAvailable;
        var buckets = holders
            .GroupBy(holder => HolderMixBuckets.Of(dimension, holder.Mix))
            .Select(group =>
                Sum(
                    group.Key.Token,
                    group.Key.Label,
                    group.Key.Order,
                    group.ToList(),
                    bySector ? sectorRows : null,
                    sectorNames
                )
            )
            .OrderBy(bucket => bucket.Order)
            .ThenBy(bucket => bucket.Label, StringComparer.Ordinal)
            .ToList();
        return new HolderFlowQuarter
        {
            ReportDate = state.ReportDate,
            Dimension = dimension,
            Grouping = grouping,
            SectorSplitAvailable = state.SectorSplitAvailable,
            ComputedAt = state.ComputedAt,
            Buckets = buckets,
            Total = Sum(
                TotalToken,
                TotalLabel,
                int.MaxValue,
                holders,
                bySector ? sectorRows : null,
                sectorNames
            ),
        };
    }

    private static HolderFlowBucket Sum(
        string token,
        string label,
        int order,
        IReadOnlyList<HolderFlowHolder> holders,
        ILookup<Guid, HolderFlowSectorRow> sectorRows,
        IReadOnlyDictionary<Guid, string> sectorNames
    )
    {
        var compared = holders.Where(holder => holder.BuyValue != null).ToList();
        return new HolderFlowBucket
        {
            Token = token,
            Label = label,
            Order = order,
            Filers = holders.Count,
            ComparedFilers = compared.Count,
            EquityAum = holders.Sum(holder => holder.EquityAum),
            PriorEquityAum = compared.Sum(holder => holder.PreviousEquityAum.GetValueOrDefault()),
            BuyValue = compared.Sum(holder => holder.BuyValue.GetValueOrDefault()),
            SellValue = compared.Sum(holder => holder.SellValue.GetValueOrDefault()),
            NetBuyers = compared.Count(holder => holder.BuyValue > holder.SellValue),
            NetSellers = compared.Count(holder => holder.SellValue > holder.BuyValue),
            UnpricedTrades = compared.Sum(holder => holder.UnpricedTradeCount.GetValueOrDefault()),
            Sectors =
                sectorRows == null ? [] : Sectors(compared, sectorRows, sectorNames ?? EmptyNames),
        };
    }

    private static readonly Dictionary<Guid, string> EmptyNames = [];

    private static List<HolderFlowSectorAmount> Sectors(
        IReadOnlyList<HolderFlowHolder> holders,
        ILookup<Guid, HolderFlowSectorRow> sectorRows,
        IReadOnlyDictionary<Guid, string> sectorNames
    ) =>
        holders
            .SelectMany(holder => sectorRows[holder.InstitutionalHolderId])
            .GroupBy(row => (row.Group, row.SectorId))
            .Select(group => new HolderFlowSectorAmount
            {
                Group = group.Key.Group,
                SectorId = group.Key.SectorId,
                Name = SectorName(group.Key.Group, group.Key.SectorId, sectorNames),
                BuyValue = group.Sum(row => row.BuyValue),
                SellValue = group.Sum(row => row.SellValue),
            })
            .OrderBy(sector => sector.Group)
            .ThenBy(sector => sector.Name, StringComparer.Ordinal)
            .ToList();

    public const string ExchangeTradedLabel = "Exchange-traded products";
    public const string UnclassifiedLabel = "Unclassified";

    // A sector the taxonomy no longer names reads Unclassified rather than a bare id.
    private static string SectorName(
        HolderFlowSectorGroup group,
        Guid? sectorId,
        IReadOnlyDictionary<Guid, string> sectorNames
    ) =>
        group switch
        {
            HolderFlowSectorGroup.ExchangeTraded => ExchangeTradedLabel,
            HolderFlowSectorGroup.Sector
                when sectorId is { } id && sectorNames.TryGetValue(id, out var name) => name,
            _ => UnclassifiedLabel,
        };
}
