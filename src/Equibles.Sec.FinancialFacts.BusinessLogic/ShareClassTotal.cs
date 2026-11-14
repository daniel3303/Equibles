namespace Equibles.Sec.FinancialFacts.BusinessLogic;

/// <summary>
/// The entity's common share count stated by one filing's per-class cover-page counts on one
/// class-of-stock axis at one date: the one rule every reader of those counts shares.
/// </summary>
internal static class ShareClassTotal
{
    internal const string CommonStockMember = "us-gaap:CommonStockMember";

    // Never part of the outstanding common shares: treasury stock, and the depositary receipt
    // count, which is stated in another unit.
    internal static readonly string[] ExcludedMembers =
    [
        "us-gaap:TreasuryStockMember",
        "us-gaap:TreasuryStockCommonMember",
        "dei:AdrMember",
    ];

    /// <summary>
    /// The sum of the classes, each member once (its first count), or null when none is a class.
    /// </summary>
    public static decimal? Of(IEnumerable<(string Member, decimal Value)> counts)
    {
        var classes = counts
            .Where(count => !ExcludedMembers.Contains(count.Member, StringComparer.Ordinal))
            .GroupBy(count => count.Member, StringComparer.Ordinal)
            .Select(member => (Member: member.Key, member.First().Value))
            .ToList();
        if (classes.Count == 0)
            return null;

        var total = classes.Sum(member => member.Value);

        // A member equal to the sum of the others is their roll-up, and counting it doubles the
        // total. Filers use the common-stock member for it, or for their main class beside the
        // others; any other member needs two beside it, since two equal classes prove nothing.
        var rollUp = classes
            .Where(member =>
                member.Value * 2 == total
                && (member.Member == CommonStockMember ? classes.Count >= 2 : classes.Count >= 3)
            )
            .Select(member => (decimal?)member.Value)
            .FirstOrDefault();
        return rollUp ?? total;
    }

    /// <summary>
    /// Whether every class the filing names on the axis is stated at <paramref name="asOf"/>: a
    /// class stated only at another date would silently drop out of the total.
    /// </summary>
    public static bool StatesEveryClassAt(
        IEnumerable<(string Member, DateOnly PeriodEnd)> axisFacts,
        DateOnly asOf
    )
    {
        var facts = axisFacts
            .Where(fact => !ExcludedMembers.Contains(fact.Member, StringComparer.Ordinal))
            .ToList();
        var current = facts
            .Where(fact => fact.PeriodEnd == asOf)
            .Select(fact => fact.Member)
            .ToHashSet(StringComparer.Ordinal);
        return facts.All(fact => current.Contains(fact.Member));
    }
}
