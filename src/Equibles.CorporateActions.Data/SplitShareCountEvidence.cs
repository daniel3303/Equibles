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

        // A ratio on the basis of 1 cannot be told apart from no change at all.
        var judged = rows.Where(row =>
                SplitEvents.Ratio(row) is { } ratio && !SplitEvents.IsSameBasis(ratio, 1m)
            )
            .ToList();
        if (ObservedRatio(rows, counts) is not { } witness)
            return Undecided(rows);

        var between = events
            .Where(other =>
                other != rows
                && other.Any(split =>
                    split.EffectiveDate > witness.Since && split.EffectiveDate <= witness.AsOf
                )
            )
            .ToList();
        if (between.Count > 0)
        {
            // Every event between the counts moves them, so only an unchanged count can speak, and
            // only when no group of the events cancels out: a 2:1 then a 1:2 leave it unchanged too.
            if (!SplitEvents.IsSameBasis(witness.Ratio, 1m) || AnyGroupCancels([rows, .. between]))
                return Undecided(rows);
            return rows.Select(row =>
                judged.Contains(row)
                    ? new SplitShareCountVerdict(
                        row,
                        StockSplitShareCountEvidence.Contradicted,
                        witness.AsOf
                    )
                    : new SplitShareCountVerdict(row, null, null)
            );
        }

        var matched = judged
            .Where(row => SplitEvents.IsSameBasis(witness.Ratio, SplitEvents.Ratio(row)!.Value))
            .ToList();
        if (matched.Count == 0 && !SplitEvents.IsSameBasis(witness.Ratio, 1m))
            return Undecided(rows);
        return rows.Select(row =>
            judged.Contains(row)
                ? new SplitShareCountVerdict(
                    row,
                    matched.Contains(row)
                        ? StockSplitShareCountEvidence.Confirmed
                        : StockSplitShareCountEvidence.Contradicted,
                    witness.AsOf
                )
                : new SplitShareCountVerdict(row, null, null)
        );
    }

    private static IEnumerable<SplitShareCountVerdict> Undecided(List<StockSplit> rows) =>
        rows.Select(row => new SplitShareCountVerdict(row, null, null));

    // More events than this between two counts are too many to tell apart.
    private const int MaxEventsBetweenCounts = 6;

    // Whether some non-empty group of the events, taking any one reported ratio per event, multiplies
    // back to the basis of 1; such a group could be the real splits behind an unchanged count.
    private static bool AnyGroupCancels(List<List<StockSplit>> events)
    {
        if (events.Count > MaxEventsBetweenCounts)
            return true;
        var eventRatios = events
            .Select(rows =>
                rows.Select(SplitEvents.Ratio)
                    .OfType<decimal>()
                    .Where(ratio => !SplitEvents.IsSameBasis(ratio, 1m))
                    .Distinct()
                    .ToList()
            )
            .ToList();
        // An event whose every ratio sits on the basis of 1 could hide inside any count.
        if (eventRatios.Any(ratios => ratios.Count == 0))
            return true;
        var products = new List<decimal>();
        foreach (var ratios in eventRatios)
        {
            products.AddRange(
                products.SelectMany(product => ratios.Select(ratio => product * ratio)).ToList()
            );
            products.AddRange(ratios);
        }
        return products.Any(product => SplitEvents.IsSameBasis(product, 1m));
    }

    // The count ratio across the event and the dates of its two counts, or null without a count on
    // each side. The earlier count must also be filed before the event, since a cover page dated
    // before a split can state the post-split count.
    private static (decimal Ratio, DateOnly Since, DateOnly AsOf)? ObservedRatio(
        List<StockSplit> rows,
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
        return (later.Shares / earlier.Shares, earlier.AsOf, later.AsOf);
    }

    private static bool IsAuthoritative(StockSplit split) =>
        split.Source is StockSplitSource.SecFiling or StockSplitSource.Manual;
}
