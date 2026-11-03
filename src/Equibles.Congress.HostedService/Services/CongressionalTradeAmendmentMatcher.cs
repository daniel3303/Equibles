using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Finds the earlier filing an amending House PTR corrects. The amendment never names that filing,
/// so the match must be unique: exactly one earlier filing of the member explains every amended
/// and deleted row, and each group of rows pairs one-to-one, or against twins filing the same
/// facts, so every pairing removes the same originals. Anything less leaves the stored trades
/// untouched.
/// </summary>
internal static class CongressionalTradeAmendmentMatcher
{
    internal static CongressionalTradeAmendment Record(
        CongressionalTrade trade,
        CongressionalTradeFilingStatus status
    ) =>
        new()
        {
            FilingKind = trade.FilingKind!.Value,
            SourceId = trade.SourceId,
            SourceRowIndex = trade.SourceRowIndex!.Value,
            CongressMemberId = trade.CongressMemberId,
            Status = status,
            FilingDate = trade.FilingDate,
            TransactionDate = trade.TransactionDate,
            TransactionType = trade.TransactionType,
            OwnerType = trade.OwnerType,
            AssetName = trade.AssetName,
            AssetType = trade.AssetType,
            FiledTicker = trade.FiledTicker,
            Subholding = trade.Subholding,
            AmountFrom = trade.AmountFrom,
            AmountTo = trade.AmountTo,
        };

    /// <param name="rows">The amended and deleted rows of one amending filing.</param>
    /// <param name="candidates">The member's stored trades from other, earlier filings.</param>
    internal static AmendmentMatch Match(
        IReadOnlyList<CongressionalTradeAmendment> rows,
        IReadOnlyList<CongressionalTrade> candidates
    )
    {
        var filings = candidates
            .GroupBy(trade => trade.SourceId, StringComparer.Ordinal)
            .Select(filing => filing.ToList())
            .ToList();

        // An exact reprint names its original more precisely than a one-field correction does, so
        // a filing whose copies explain every row outranks filings that need a changed field.
        var exact = Decide(rows, filings, exactOnly: true);
        return exact.Verdict == CongressionalTradeAmendmentVerdict.NoPredecessor
            ? Decide(rows, filings, exactOnly: false)
            : exact;
    }

    private static AmendmentMatch Decide(
        IReadOnlyList<CongressionalTradeAmendment> rows,
        IReadOnlyList<List<CongressionalTrade>> filings,
        bool exactOnly
    )
    {
        var outcomes = filings.Select(filing => Pair(rows, filing, exactOnly)).ToList();
        var explanations = outcomes
            .Where(outcome => outcome.Outcome == PairOutcome.Explains)
            .Select(outcome => outcome.Pairs)
            .ToList();
        var ambiguous = outcomes.Any(outcome => outcome.Outcome == PairOutcome.Ambiguous);

        if (explanations.Count == 1 && !ambiguous)
            return new AmendmentMatch(CongressionalTradeAmendmentVerdict.Matched, explanations[0]);
        return explanations.Count == 0 && !ambiguous
            ? new AmendmentMatch(CongressionalTradeAmendmentVerdict.NoPredecessor, [])
            : new AmendmentMatch(CongressionalTradeAmendmentVerdict.Ambiguous, []);
    }

    private static (PairOutcome Outcome, IReadOnlyList<AmendmentPair> Pairs) Pair(
        IReadOnlyList<CongressionalTradeAmendment> rows,
        IReadOnlyList<CongressionalTrade> filing,
        bool exactOnly
    )
    {
        var edges = new List<int>[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            edges[i] = CandidatesFor(rows[i], filing, exactOnly);
            if (edges[i].Count == 0)
                return (PairOutcome.DoesNotExplain, []);
        }

        // Rows and candidates that share an edge form one group. A group with as many candidates
        // as rows and a perfect pairing removes the same originals whichever pairing is chosen;
        // Castor's three year-typo rows each match all three originals and resolve this way.
        var result = new List<AmendmentPair>();
        foreach (var group in ConnectedGroups(edges))
        {
            var groupCandidates = group.SelectMany(row => edges[row]).Distinct().ToList();
            if (groupCandidates.Count > group.Count && AreTwins(groupCandidates, filing))
            {
                // Twins file the same facts, so whichever rows are superseded the stored trades
                // read the same; row order keeps the choice stable across cycles.
                var twins = groupCandidates.OrderBy(index => filing[index].SourceRowIndex);
                result.AddRange(
                    group
                        .OrderBy(row => rows[row].SourceRowIndex)
                        .Zip(twins, (row, twin) => new AmendmentPair(rows[row], filing[twin]))
                );
                continue;
            }

            if (groupCandidates.Count != group.Count)
                return (PairOutcome.Ambiguous, []);

            var assignment = PerfectPairing(group, edges);
            if (assignment == null)
                return (PairOutcome.Ambiguous, []);

            result.AddRange(
                assignment.Select(pair => new AmendmentPair(rows[pair.Key], filing[pair.Value]))
            );
        }

        return (PairOutcome.Explains, result);
    }

    // A deleted row reprints its original exactly. An amended row prefers an exact copy (only an
    // unstored field such as the description changed), else a row with one filed field changed.
    private static List<int> CandidatesFor(
        CongressionalTradeAmendment row,
        IReadOnlyList<CongressionalTrade> filing,
        bool exactOnly
    )
    {
        var exact = new List<int>();
        var oneField = new List<int>();
        for (var i = 0; i < filing.Count; i++)
        {
            var differences = DifferingFields(row, filing[i]);
            if (differences == 0)
                exact.Add(i);
            else if (differences == 1)
                oneField.Add(i);
        }

        if (exact.Count > 0 || exactOnly || row.Status == CongressionalTradeFilingStatus.Deleted)
            return exact;
        return oneField;
    }

    private static bool AreTwins(List<int> candidates, IReadOnlyList<CongressionalTrade> filing)
    {
        var first = Record(filing[candidates[0]], CongressionalTradeFilingStatus.Unstated);
        return candidates.Skip(1).All(index => DifferingFields(first, filing[index]) == 0);
    }

    // The filed ticker is the asset's identity and must agree; each other filed fact counts once.
    internal static int DifferingFields(CongressionalTradeAmendment row, CongressionalTrade trade)
    {
        if (!string.Equals(row.FiledTicker, trade.FiledTicker, StringComparison.Ordinal))
            return int.MaxValue;

        var differences = 0;
        if (row.TransactionDate != trade.TransactionDate)
            differences++;
        if (row.TransactionType != trade.TransactionType)
            differences++;
        if (!string.Equals(row.OwnerType, trade.OwnerType, StringComparison.Ordinal))
            differences++;
        if (!string.Equals(row.Subholding, trade.Subholding, StringComparison.Ordinal))
            differences++;
        if (row.AmountFrom != trade.AmountFrom || row.AmountTo != trade.AmountTo)
            differences++;
        if (
            !string.Equals(row.AssetName, trade.AssetName, StringComparison.Ordinal)
            || !string.Equals(row.AssetType, trade.AssetType, StringComparison.Ordinal)
        )
            differences++;
        return differences;
    }

    private static List<List<int>> ConnectedGroups(List<int>[] edges)
    {
        var groups = new List<List<int>>();
        var seen = new bool[edges.Length];
        for (var start = 0; start < edges.Length; start++)
        {
            if (seen[start])
                continue;

            var group = new List<int>();
            var candidates = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0)
            {
                var row = queue.Dequeue();
                group.Add(row);
                foreach (var candidate in edges[row])
                    candidates.Add(candidate);
                for (var other = 0; other < edges.Length; other++)
                {
                    if (!seen[other] && edges[other].Any(candidates.Contains))
                    {
                        seen[other] = true;
                        queue.Enqueue(other);
                    }
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    // Kuhn's augmenting paths; groups are a handful of rows, so the cubic bound is irrelevant.
    private static Dictionary<int, int> PerfectPairing(List<int> group, List<int>[] edges)
    {
        var rowFor = new Dictionary<int, int>();
        foreach (var row in group)
        {
            if (!Augment(row, edges, rowFor, []))
                return null;
        }

        return rowFor.ToDictionary(pair => pair.Value, pair => pair.Key);
    }

    private static bool Augment(
        int row,
        List<int>[] edges,
        Dictionary<int, int> rowFor,
        HashSet<int> visited
    )
    {
        var candidates = edges[row];
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!visited.Add(candidate))
                continue;
            if (
                !rowFor.TryGetValue(candidate, out var holder)
                || Augment(holder, edges, rowFor, visited)
            )
            {
                rowFor[candidate] = row;
                return true;
            }
        }

        return false;
    }

    private enum PairOutcome
    {
        DoesNotExplain,
        Explains,
        Ambiguous,
    }
}

internal sealed record AmendmentPair(
    CongressionalTradeAmendment Amendment,
    CongressionalTrade Superseded
);

internal sealed record AmendmentMatch(
    CongressionalTradeAmendmentVerdict Verdict,
    IReadOnlyList<AmendmentPair> Pairs
);
