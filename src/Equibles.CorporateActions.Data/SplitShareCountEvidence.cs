using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// Judges captured split ratios against the issuer's own share counts: a split changes the count, a
/// price factor (a spin-off, a depositary ratio change) does not.
/// </summary>
public static class SplitShareCountEvidence
{
    /// <summary>Returns one verdict per row, for the issuer's complete split history.</summary>
    public static List<SplitShareCountVerdict> Evaluate(
        IReadOnlyCollection<StockSplit> splits,
        IReadOnlyCollection<ShareCountObservation> counts
    )
    {
        var events = SplitEvents.Group(splits);
        return events.SelectMany(rows => Judge(rows, events, counts)).ToList();
    }

    private static IEnumerable<SplitShareCountVerdict> Judge(
        List<StockSplit> rows,
        List<List<StockSplit>> events,
        IReadOnlyCollection<ShareCountObservation> counts
    )
    {
        // A filed or hand-entered ratio states the event; other rows of it agree or contradict it.
        var stated = rows.Where(IsAuthoritative)
            .Select(SplitEvents.Ratio)
            .OfType<decimal>()
            .ToList();
        if (stated.Count > 0)
            return rows.Select(row =>
                SplitEvents.Ratio(row) is { } ratio
                    ? new SplitShareCountVerdict(
                        row,
                        stated.Any(statedRatio => SplitEvents.IsSameBasis(ratio, statedRatio))
                            ? StockSplitShareCountEvidence.Confirmed
                            : StockSplitShareCountEvidence.Contradicted,
                        null
                    )
                    : new SplitShareCountVerdict(row, null, null)
            );

        var observed = ObservedRatio(rows, events, counts);
        // A ratio on the basis of 1 cannot be told apart from no change at all.
        var judged = rows.Where(row =>
                SplitEvents.Ratio(row) is { } ratio && !SplitEvents.IsSameBasis(ratio, 1m)
            )
            .ToList();
        var matched = observed is { } witness
            ? judged
                .Where(row => SplitEvents.IsSameBasis(witness.Ratio, SplitEvents.Ratio(row)!.Value))
                .ToList()
            : [];
        var decided =
            observed is { } unchanged
            && (matched.Count > 0 || SplitEvents.IsSameBasis(unchanged.Ratio, 1m));
        return rows.Select(row =>
            decided && judged.Contains(row)
                ? new SplitShareCountVerdict(
                    row,
                    matched.Contains(row)
                        ? StockSplitShareCountEvidence.Confirmed
                        : StockSplitShareCountEvidence.Contradicted,
                    observed!.Value.AsOf
                )
                : new SplitShareCountVerdict(row, null, null)
        );
    }

    // The count ratio across the event, or null when no pair brackets it alone. The earlier count must
    // also be filed before the event, since a cover page dated before a split can state the post-split count.
    private static (decimal Ratio, DateOnly AsOf)? ObservedRatio(
        List<StockSplit> rows,
        List<List<StockSplit>> events,
        IReadOnlyCollection<ShareCountObservation> counts
    )
    {
        var start = rows[0].EffectiveDate;
        var end = rows[^1].EffectiveDate;
        var before = counts
            .Where(count => count.AsOf < start && count.FiledDate < start && count.Shares > 0m)
            .OrderByDescending(count => count.AsOf)
            .ThenByDescending(count => count.FiledDate)
            .Select(count => (ShareCountObservation?)count)
            .FirstOrDefault();
        var after = counts
            .Where(count => count.AsOf >= end && count.Shares > 0m)
            .OrderBy(count => count.AsOf)
            .ThenByDescending(count => count.FiledDate)
            .Select(count => (ShareCountObservation?)count)
            .FirstOrDefault();
        if (before is not { } earlier || after is not { } later)
            return null;
        // Another event between the two counts moves them too.
        if (
            events.Any(other =>
                other != rows
                && other.Any(split =>
                    split.EffectiveDate > earlier.AsOf && split.EffectiveDate <= later.AsOf
                )
            )
        )
            return null;
        return (later.Shares / earlier.Shares, later.AsOf);
    }

    private static bool IsAuthoritative(StockSplit split) =>
        split.Source is StockSplitSource.SecFiling or StockSplitSource.Manual;
}
