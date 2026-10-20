using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.FundOwnership;

/// <summary>A listing's registered-fund ownership by fund type over its last computed quarters.</summary>
public sealed class FundOwnershipHistory
{
    public string ListedTicker { get; init; }

    /// <summary>True for an ETF series or secondary security read on its exact listing.</summary>
    public bool ExactListing { get; init; }

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<FundOwnershipQuarter> Quarters { get; init; } = [];

    /// <summary>Today's shares outstanding of the presentation security; null when unknown.</summary>
    public long? SharesOutstanding { get; init; }

    /// <summary>Why there is nothing to show, or null.</summary>
    public string Unavailable { get; init; }

    public FundOwnershipQuarter Latest => Quarters.Count == 0 ? null : Quarters[^1];
}

/// <summary>One quarter of registered-fund ownership, every bucket present even when empty.</summary>
public sealed class FundOwnershipQuarter
{
    public DateOnly ReportDate { get; init; }
    public IReadOnlyList<FundOwnershipBucketShare> Buckets { get; init; } = [];
    public FundOwnershipBucketShare Total { get; init; }

    /// <summary>
    /// True when a split after the quarter end cannot be attributed, so its counts are on the quarter
    /// end's basis and no percentage of today's shares outstanding is given.
    /// </summary>
    public bool SplitUnresolved { get; init; }

    /// <summary>When the quarter's rows were published.</summary>
    public DateTime? ComputedAt { get; init; }

    public long IndexShares =>
        Buckets.Where(bucket => FundOwnershipBuckets.IsIndex(bucket.Bucket)).Sum(b => b.Shares);

    /// <summary>Index funds' and index ETFs' share of shares outstanding, percent; null when unknown.</summary>
    public decimal? IndexPercentOfOutstanding =>
        Buckets.Any(bucket => bucket.PercentOfOutstanding == null)
            ? null
            : Buckets
                .Where(bucket => FundOwnershipBuckets.IsIndex(bucket.Bucket))
                .Sum(bucket => bucket.PercentOfOutstanding.Value);

    public FundOwnershipBucketShare Bucket(FundOwnershipBucket bucket) =>
        Buckets.First(row => row.Bucket == bucket);
}

/// <summary>One fund-type bucket's positions in a quarter, restated onto today's split basis.</summary>
public sealed class FundOwnershipBucketShare
{
    public FundOwnershipBucket Bucket { get; init; }
    public string Token { get; init; }
    public string Label { get; init; }
    public long Shares { get; init; }

    /// <summary>Value in US dollars as each fund filed it, at its own report date.</summary>
    public decimal Value { get; init; }

    public int SeriesCount { get; init; }
    public DateOnly? OldestReportPeriod { get; init; }
    public DateOnly? NewestReportPeriod { get; init; }

    /// <summary>Percent of today's shares outstanding; null when unknown or the split basis is unresolved.</summary>
    public decimal? PercentOfOutstanding { get; init; }

    /// <summary>Shares kept as filed because a split before the quarter end could not be attributed.</summary>
    public long SplitUnresolvedShares { get; init; }
}

/// <summary>A 13F filer's position in a listing and the part its linked registered funds hold.</summary>
public sealed record AdvisedFundShares(
    Guid InstitutionalHolderId,
    DateOnly ReportDate,
    long HolderShares,
    long FundShares,
    long IndexFundShares,
    int SeriesCount,
    int IndexSeriesCount,
    bool IsCapped
);
