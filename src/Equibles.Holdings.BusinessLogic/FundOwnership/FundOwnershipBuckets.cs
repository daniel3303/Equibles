using Equibles.Holdings.Data.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.BusinessLogic.FundOwnership;

/// <summary>
/// The fund-type bucket of a registered fund series, read only from its Form N-CEN report: Item C.3
/// fund types and Item E exchange listings. A name never decides a bucket.
/// </summary>
public static class FundOwnershipBuckets
{
    public sealed record Bucket(FundOwnershipBucket Value, string Token, string Label, int Order);

    private static readonly Bucket[] All =
    [
        new(FundOwnershipBucket.IndexFund, "idxfund", "Index funds", 0),
        new(FundOwnershipBucket.IndexEtf, "idxetf", "Index ETFs", 1),
        new(FundOwnershipBucket.ActiveEtf, "actetf", "Active ETFs", 2),
        new(FundOwnershipBucket.ActiveFund, "actfund", "Active funds", 3),
        new(FundOwnershipBucket.Unclassified, "unclassified", "Unclassified", 4),
    ];

    public static IReadOnlyList<Bucket> Ordered => All;

    public static Bucket Of(FundOwnershipBucket bucket) => All.Single(b => b.Value == bucket);

    /// <summary>
    /// The bucket of a series whose latest N-CEN fund report states <paramref name="fundTypes"/>;
    /// null fund types mean no report identifies the series. An index fund reports Item C.3.a's
    /// "Index Fund" box; a fund that only seeks a multiple or the inverse of an index does not
    /// track it one for one, so it is not counted as an index fund. Exchange-traded means the ETF or
    /// exchange-traded managed fund box, or an Item E exchange listing.
    /// </summary>
    public static FundOwnershipBucket Classify(NCenFundTypes? fundTypes, bool hasExchangeListing)
    {
        if (fundTypes is not { } types)
            return FundOwnershipBucket.Unclassified;
        var exchangeTraded =
            hasExchangeListing
            || (
                types & (NCenFundTypes.ExchangeTradedFund | NCenFundTypes.ExchangeTradedManagedFund)
            ) != 0;
        var index = types.HasFlag(NCenFundTypes.IndexFund);
        return index && exchangeTraded ? FundOwnershipBucket.IndexEtf
            : index ? FundOwnershipBucket.IndexFund
            : exchangeTraded ? FundOwnershipBucket.ActiveEtf
            : FundOwnershipBucket.ActiveFund;
    }

    /// <summary>Index funds and index ETFs: the buckets that buy a security because an index holds it.</summary>
    public static bool IsIndex(FundOwnershipBucket bucket) =>
        bucket is FundOwnershipBucket.IndexFund or FundOwnershipBucket.IndexEtf;
}
