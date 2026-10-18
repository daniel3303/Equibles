using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Splits a stock's 13F holders into the buckets of every <see cref="HolderMixDimension"/>, each
/// quarter, with each bucket's like-for-like change. Every amount is added to one bucket of every
/// dimension and to the total, so a dimension's buckets always sum to the total.
/// </summary>
public static class HolderMixCalculator
{
    private static readonly HolderMixDimension[] Dimensions = Enum.GetValues<HolderMixDimension>();

    /// <summary>
    /// Builds every quarter after the first of <paramref name="ordered"/> (oldest first); the first
    /// only serves as the previous side of the second.
    /// </summary>
    /// <param name="comparisons">The like-for-like comparison of each quarter with the one before.</param>
    /// <param name="measuredQuarters">The measured quarter each report date reads its bands from.</param>
    /// <param name="holderAt">A filer's bucket inputs in a measured quarter (null: none measured).</param>
    /// <param name="combinedReportDate">The open quarter shown as the combined view, if any.</param>
    public static List<HolderMixQuarter> Build(
        IReadOnlyList<DateOnly> ordered,
        IReadOnlyList<HolderMixPosition> positions,
        IReadOnlyDictionary<DateOnly, HoldingsComparisonStatus> comparisons,
        IReadOnlyDictionary<DateOnly, DateOnly?> measuredQuarters,
        Func<Guid, DateOnly?, HolderMixHolder> holderAt,
        DateOnly? combinedReportDate = null
    )
    {
        var byDate = positions
            .GroupBy(position => position.ReportDate)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(position => position.InstitutionalHolderId)
            );
        var quarters = new List<HolderMixQuarter>();
        for (var index = 1; index < ordered.Count; index++)
        {
            var reportDate = ordered[index];
            var previousDate = ordered[index - 1];
            var comparison = comparisons.GetValueOrDefault(reportDate);
            var measured = measuredQuarters.GetValueOrDefault(reportDate);
            quarters.Add(
                BuildQuarter(
                    reportDate,
                    byDate.GetValueOrDefault(reportDate) ?? [],
                    byDate.GetValueOrDefault(previousDate) ?? [],
                    comparison,
                    measured,
                    holderId => holderAt(holderId, measured),
                    reportDate == combinedReportDate
                )
            );
        }
        return quarters;
    }

    // The style buckets of books measured as actively managed: index-like, Unknown and unmeasured
    // books are left out of the active holders.
    private static readonly HashSet<string> ActiveStyleTokens = new()
    {
        HolderBucketTokens.StyleToken(HolderStyleBand.SystematicLike),
        HolderBucketTokens.StyleToken(HolderStyleBand.Concentrated),
        HolderBucketTokens.StyleToken(HolderStyleBand.DiversifiedActive),
    };

    /// <summary>
    /// The quarter's style buckets measured as actively managed, summed into one bucket; null when
    /// no quarter is measured on or before it.
    /// </summary>
    public static HolderMixBucket ActiveHolders(HolderMixQuarter quarter)
    {
        if (quarter.MeasuredQuarter == null)
            return null;
        var active = quarter
            .Dimensions[HolderMixDimension.Style]
            .Where(bucket => ActiveStyleTokens.Contains(bucket.Token))
            .ToList();
        var compared = quarter.Total.LikeForLikeCurrent != null;
        return new HolderMixBucket
        {
            Token = "active",
            Label = "Active holders",
            Shares = active.Sum(bucket => bucket.Shares),
            Value = active.Sum(bucket => bucket.Value),
            Filers = active.Sum(bucket => bucket.Filers),
            CallShares = active.Sum(bucket => bucket.CallShares),
            CallValue = active.Sum(bucket => bucket.CallValue),
            PutShares = active.Sum(bucket => bucket.PutShares),
            PutValue = active.Sum(bucket => bucket.PutValue),
            LikeForLikeCurrent = compared
                ? active.Sum(bucket => bucket.LikeForLikeCurrent ?? 0)
                : null,
            LikeForLikePrevious = compared
                ? active.Sum(bucket => bucket.LikeForLikePrevious ?? 0)
                : null,
            ExcludedCurrentShares = active.Sum(bucket => bucket.ExcludedCurrentShares),
            ExcludedPreviousShares = active.Sum(bucket => bucket.ExcludedPreviousShares),
            NewFilers = active.Sum(bucket => bucket.NewFilers),
            ExitedFilers = active.Sum(bucket => bucket.ExitedFilers),
        };
    }

    private static HolderMixQuarter BuildQuarter(
        DateOnly reportDate,
        Dictionary<Guid, HolderMixPosition> current,
        Dictionary<Guid, HolderMixPosition> previous,
        HoldingsComparisonStatus comparison,
        DateOnly? measured,
        Func<Guid, HolderMixHolder> holderOf,
        bool combined
    )
    {
        var compared = comparison is { ConsecutiveQuarters: true };
        var mix = new Accumulator(holderOf, compared);
        var carryForward = compared && combined;
        // In the combined view a holder yet to file carries its prior position; otherwise a
        // holder with no observed 13F this quarter lapses out of the comparison.
        IReadOnlySet<Guid> carried = carryForward
            ? comparison.MissingCurrentFilers
            : new HashSet<Guid>();
        IReadOnlySet<Guid> lapsed =
            compared && !carryForward ? comparison.MissingCurrentFilers : new HashSet<Guid>();

        var book = new Dictionary<Guid, HolderMixPosition>(current);
        foreach (var holderId in carried)
            if (previous.TryGetValue(holderId, out var position))
                book.TryAdd(holderId, position);
        foreach (var (holderId, position) in book)
            mix.Add(
                holderId,
                bucket =>
                {
                    bucket.Shares += position.Shares;
                    bucket.Value += position.Value;
                    bucket.CallShares += position.CallShares;
                    bucket.CallValue += position.CallValue;
                    bucket.PutShares += position.PutShares;
                    bucket.PutValue += position.PutValue;
                    if (position.Shares > 0)
                        bucket.Filers++;
                }
            );

        if (compared)
            AddLikeForLike(mix, comparison, book, previous, lapsed, holderOf);

        return new HolderMixQuarter
        {
            ReportDate = reportDate,
            IsCombined = carryForward,
            MeasuredQuarter = measured,
            // Carried filers are compared, so the combined view's note excludes nobody for them.
            Comparison = carryForward
                ? comparison with
                {
                    MissingCurrentFilers = new HashSet<Guid>(),
                }
                : comparison,
            Total = mix.Total,
            Dimensions = Dimensions.ToDictionary(dimension => dimension, mix.Buckets),
        };
    }

    private static void AddLikeForLike(
        Accumulator mix,
        HoldingsComparisonStatus comparison,
        Dictionary<Guid, HolderMixPosition> book,
        Dictionary<Guid, HolderMixPosition> previous,
        IReadOnlySet<Guid> lapsed,
        Func<Guid, HolderMixHolder> holderOf
    )
    {
        long SharesNow(Guid holderId) => book.GetValueOrDefault(holderId)?.Shares ?? 0;
        long SharesBefore(Guid holderId) => previous.GetValueOrDefault(holderId)?.Shares ?? 0;

        var groups = comparison.Successions.Where(group => group.Comparable).ToList();
        var grouped = groups
            .SelectMany(group => group.Predecessors.Keys.Concat(group.Successors.Keys))
            .ToHashSet();

        foreach (
            var holderId in book.Keys.Where(id => !comparison.MissingPreviousFilers.Contains(id))
        )
            mix.Add(holderId, bucket => bucket.LikeForLikeCurrent += SharesNow(holderId));
        foreach (
            var holderId in previous.Keys.Where(id => !lapsed.Contains(id) && !grouped.Contains(id))
        )
            mix.Add(holderId, bucket => bucket.LikeForLikePrevious += SharesBefore(holderId));

        foreach (var holderId in comparison.MissingPreviousFilers.Where(book.ContainsKey))
            mix.Add(holderId, bucket => bucket.ExcludedCurrentShares += SharesNow(holderId));
        foreach (var holderId in lapsed.Where(previous.ContainsKey))
            mix.Add(holderId, bucket => bucket.ExcludedPreviousShares += SharesBefore(holderId));

        // A filer compared on its own opens or closes a position in its current bucket.
        var single = book
            .Keys.Concat(previous.Keys)
            .Where(id =>
                !grouped.Contains(id)
                && !comparison.MissingPreviousFilers.Contains(id)
                && !lapsed.Contains(id)
            )
            .Distinct();
        foreach (var holderId in single)
        {
            var heldNow = SharesNow(holderId) > 0;
            var heldBefore = SharesBefore(holderId) > 0;
            if (heldNow && !heldBefore)
                mix.Add(holderId, bucket => bucket.NewFilers++);
            else if (heldBefore && !heldNow)
                mix.Add(holderId, bucket => bucket.ExitedFilers++);
        }

        foreach (var group in groups)
            AddSuccessionGroup(mix, group, SharesNow, SharesBefore, holderOf);
    }

    // A group compared across a CIK change reads in its successors' buckets on both sides: the
    // predecessors' shares split by what each successor holds now, or go whole to the successor
    // with the largest book when none holds the stock.
    private static void AddSuccessionGroup(
        Accumulator mix,
        HoldingsSuccessionGroup group,
        Func<Guid, long> sharesNow,
        Func<Guid, long> sharesBefore,
        Func<Guid, HolderMixHolder> holderOf
    )
    {
        var successors = group.Successors.Keys.OrderBy(id => id).ToList();
        var before = group.Predecessors.Keys.Sum(sharesBefore);
        var weights = successors.Select(id => Math.Max(sharesNow(id), 0)).ToList();
        var held = weights.Any(weight => weight > 0);
        var largest = successors
            .OrderByDescending(id => holderOf(id).EquityAum ?? -1)
            .ThenBy(id => id)
            .First();
        if (!held)
            weights = successors.Select(id => id == largest ? 1L : 0L).ToList();
        var shares = Allocate(before, weights);
        for (var index = 0; index < successors.Count; index++)
        {
            var amount = shares[index];
            if (amount != 0)
                mix.Add(successors[index], bucket => bucket.LikeForLikePrevious += amount);
        }

        if (before <= 0)
        {
            foreach (var successor in successors.Where(id => sharesNow(id) > 0))
                mix.Add(successor, bucket => bucket.NewFilers++);
        }
        else if (!held)
            mix.Add(largest, bucket => bucket.ExitedFilers++);
    }

    // Splits a whole number of shares in proportion to the weights, handing the remainders to the
    // largest fractions so the parts sum exactly to the amount.
    internal static long[] Allocate(long amount, IReadOnlyList<long> weights)
    {
        var parts = new long[weights.Count];
        var total = weights.Sum();
        if (total <= 0 || amount == 0)
            return parts;
        var remainders = new decimal[weights.Count];
        for (var index = 0; index < weights.Count; index++)
        {
            var exact = (decimal)amount * weights[index] / total;
            parts[index] = (long)decimal.Truncate(exact);
            remainders[index] = exact - parts[index];
        }
        var left = amount - parts.Sum();
        var step = Math.Sign(left);
        foreach (
            var index in Enumerable
                .Range(0, weights.Count)
                .OrderByDescending(index => Math.Abs(remainders[index]))
                .ThenBy(index => index)
                .Take((int)Math.Abs(left))
        )
            parts[index] += step;
        return parts;
    }

    // Applies each amount to one bucket of every dimension and to the total. With a comparison
    // every bucket starts its like-for-like pair at zero, else it stays null.
    private sealed class Accumulator
    {
        private readonly Func<Guid, HolderMixHolder> _holderOf;
        private readonly bool _compared;
        private readonly Dictionary<Guid, HolderMixHolder> _holders = [];
        private readonly Dictionary<
            (HolderMixDimension, string),
            (HolderMixBucket Bucket, int Order)
        > _buckets = [];

        public Accumulator(Func<Guid, HolderMixHolder> holderOf, bool compared)
        {
            _holderOf = holderOf;
            _compared = compared;
            Total = NewBucket("all", "All holders");
        }

        public HolderMixBucket Total { get; }

        public void Add(Guid holderId, Action<HolderMixBucket> apply)
        {
            if (!_holders.TryGetValue(holderId, out var holder))
                _holders[holderId] = holder = _holderOf(holderId);
            apply(Total);
            foreach (var dimension in Dimensions)
            {
                var key = HolderMixBuckets.Of(dimension, holder);
                if (!_buckets.TryGetValue((dimension, key.Token), out var entry))
                    _buckets[(dimension, key.Token)] = entry = (
                        NewBucket(key.Token, key.Label),
                        key.Order
                    );
                apply(entry.Bucket);
            }
        }

        public IReadOnlyList<HolderMixBucket> Buckets(HolderMixDimension dimension) =>
            _buckets
                .Where(entry => entry.Key.Item1 == dimension)
                .OrderBy(entry => entry.Value.Order)
                .Select(entry => entry.Value.Bucket)
                .ToList();

        private HolderMixBucket NewBucket(string token, string label) =>
            new()
            {
                Token = token,
                Label = label,
                LikeForLikeCurrent = _compared ? 0 : null,
                LikeForLikePrevious = _compared ? 0 : null,
            };
    }
}
