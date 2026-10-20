using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings.Ownership;

/// <summary>Builds lane rows the way the contributors emit them, for the arrangement rules.</summary>
internal static class OwnershipRows
{
    public static readonly DateOnly Today = new(2026, 10, 20);
    public static readonly DateOnly Q2 = new(2026, 6, 30);

    public static StockHolderRow Form13F(string cik, long shares, DateOnly? asOf = null) =>
        Row(StockHolderSource.Form13F, cik, shares, asOf ?? Q2, holderId: HolderId(cik));

    public static StockHolderRow Schedule13G(
        string cik,
        long shares,
        DateOnly eventDate,
        params string[] personCiks
    ) => Row(StockHolderSource.Schedule13G, cik, shares, eventDate, HolderId(cik), personCiks);

    public static StockHolderRow Schedule13D(
        string cik,
        long shares,
        DateOnly eventDate,
        params string[] personCiks
    ) => Row(StockHolderSource.Schedule13D, cik, shares, eventDate, HolderId(cik), personCiks);

    public static StockHolderRow Insider(string cik, long shares, DateOnly? asOf = null) =>
        Row(StockHolderSource.Insider, cik, shares, asOf ?? new DateOnly(2026, 9, 1), null);

    public static StockHolderRow Fund(string seriesId, long shares, string adviserCik = null) =>
        new()
        {
            HolderKey = new StockHolderKey(StockHolderKeyKind.SeriesId, seriesId),
            Source = StockHolderSource.Fund,
            Name = "Fund " + seriesId,
            SeriesId = seriesId,
            AdviserHolderId = adviserCik == null ? null : HolderId(adviserCik),
            Shares = shares,
            AsOf = new DateOnly(2026, 7, 31),
            EntityType = HolderEntityType.RegisteredFund,
            Flags = StockHolderRowFlags.NotSummed,
        };

    public static HolderRollup ReportsThrough(string childCik, string parentCik) =>
        new()
        {
            Cik = childCik,
            ReportingParentCik = parentCik,
            UltimateCik = parentCik,
            Basis = HolderRollupBasis.ReportingParent,
        };

    public static HolderRollup OwnedBy(string childCik, string ultimateCik) =>
        new()
        {
            Cik = childCik,
            UltimateCik = ultimateCik,
            Basis = HolderRollupBasis.GleifUltimateParent,
        };

    public static Guid HolderId(string cik) => new(int.Parse(cik), 0, 0, new byte[8]);

    private static StockHolderRow Row(
        StockHolderSource source,
        string cik,
        long shares,
        DateOnly asOf,
        Guid? holderId,
        string[] personCiks = null
    ) =>
        new()
        {
            HolderKey = new StockHolderKey(StockHolderKeyKind.Cik, cik),
            Source = source,
            Name = "Holder " + cik,
            Cik = cik,
            InstitutionalHolderId = holderId,
            ReportingPersonCiks = personCiks ?? [],
            Shares = shares,
            FiledShares = shares,
            AsOf = asOf,
            FilingDate = asOf.AddDays(10),
            Flags =
                source == StockHolderSource.Insider
                    ? StockHolderRowFlags.IssuerWide
                    : StockHolderRowFlags.None,
        };
}
