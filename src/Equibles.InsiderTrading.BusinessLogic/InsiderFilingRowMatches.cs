using Equibles.InsiderTrading.Data.Models;

namespace Equibles.InsiderTrading.BusinessLogic;

internal static class InsiderFilingRowMatches
{
    // Only fields the historical replay never rewrote can establish a legacy row's identity.
    internal static Dictionary<Guid, InsiderTransaction> Match(
        IReadOnlyList<InsiderTransaction> stored,
        IReadOnlyList<InsiderTransaction> source
    )
    {
        var byOrder = source.ToDictionary(t => t.TransactionOrder);
        if (
            stored.Count == source.Count
            && stored.All(row =>
                byOrder.TryGetValue(row.TransactionOrder, out var candidate)
                && Key(row.SecurityTitle, row.Shares, row.ReportedPricePerShare)
                    == Key(candidate.SecurityTitle, candidate.Shares, candidate.PricePerShare)
            )
        )
            return stored.ToDictionary(row => row.Id, row => byOrder[row.TransactionOrder]);

        var sourceGroups = source
            .GroupBy(row => Key(row.SecurityTitle, row.Shares, row.PricePerShare))
            .ToDictionary(g => g.Key, g => g.ToList());
        var matches = new Dictionary<Guid, InsiderTransaction>();
        foreach (
            var group in stored.GroupBy(row =>
                Key(row.SecurityTitle, row.Shares, row.ReportedPricePerShare)
            )
        )
        {
            if (
                group.Count() == 1
                && sourceGroups.TryGetValue(group.Key, out var candidates)
                && candidates.Count == 1
            )
                matches[group.Single().Id] = candidates[0];
        }
        return matches;
    }

    private static (string Title, long Shares, decimal Price) Key(
        string title,
        long shares,
        decimal price
    ) => (title, shares, price);
}
