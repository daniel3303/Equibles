using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Folds one filer's priced trades into its buying and selling per sector, so flows can be split
/// by sector without keeping any per-security trade past the filer.
/// </summary>
public static class HolderSectorFlowCalculator
{
    /// <summary>The first calculation version whose quarters carry sector flow rows.</summary>
    public const int FirstVersion = 4;

    /// <summary>
    /// One row per sector the filer traded. A security in <paramref name="exchangeTraded"/> is an
    /// exchange-traded product whatever its issuer; an issuer missing from
    /// <paramref name="issuerSectors"/> is Unclassified.
    /// </summary>
    public static List<HolderQuarterlySectorFlow> Flows(
        Guid holderId,
        DateOnly reportDate,
        IReadOnlyList<HolderSecurityTrade> trades,
        IReadOnlyDictionary<Guid, Guid> issuerSectors,
        IReadOnlySet<HolderSecurityKey> exchangeTraded
    ) =>
        trades
            .Where(trade => trade.BuyValue > 0 || trade.SellValue > 0)
            .GroupBy(trade => SectorOf(trade.Security, issuerSectors, exchangeTraded))
            .Select(group => new HolderQuarterlySectorFlow
            {
                InstitutionalHolderId = holderId,
                ReportDate = reportDate,
                SectorGroup = group.Key.Group,
                SectorId = group.Key.SectorId,
                BuyValue = group.Sum(trade => trade.BuyValue),
                SellValue = group.Sum(trade => trade.SellValue),
            })
            .OrderBy(flow => flow.SectorGroup)
            .ThenBy(flow => flow.SectorId)
            .ToList();

    // Keyed by listed security, so a bank's ETN never moves its common stock out of its sector.
    private static HolderFlowSector SectorOf(
        HolderSecurityKey security,
        IReadOnlyDictionary<Guid, Guid> issuerSectors,
        IReadOnlySet<HolderSecurityKey> exchangeTraded
    ) =>
        exchangeTraded.Contains(security) ? HolderFlowSector.ExchangeTraded
        : issuerSectors.TryGetValue(security.IssuerId, out var sectorId)
            ? new HolderFlowSector(HolderFlowSectorGroup.Sector, sectorId)
        : HolderFlowSector.Unclassified;
}
