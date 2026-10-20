using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Carries a filer's estimated cost per share forward one quarter by the average-cost method: a buy
/// adds its shares at the security's quarter average price, a sale leaves the cost per share
/// unchanged, and a split between the quarters restates the carried cost onto the new basis. A
/// position opened inside the quarter starts at the quarter average price; a full sale ends it.
/// </summary>
/// <remarks>
/// Only the quarter average price trusted by <see cref="QuarterAveragePriceCalculator"/> costs a
/// buy, never the quarter-end price, so every estimate is a mean traded price. Once a buy cannot
/// be costed the position stays unavailable until it is fully sold, since any later figure would
/// blend in an unknown price.
/// </remarks>
public static class HolderCostBasisCalculator
{
    /// <summary>The <c>CalculationVersion</c> that first stored cost slots and holder signals.</summary>
    public const int FirstVersion = 7;

    /// <summary>
    /// Each security held in <paramref name="current"/> with its cost slot. A null
    /// <paramref name="previous"/> (no comparable 13F the quarter before) leaves every entry
    /// unknown. <paramref name="previous"/> is already on the current basis, and
    /// <paramref name="previousShareFactors"/> carries the factor that put it there, so the cost
    /// divides by it. A security missing from <paramref name="previousSlots"/> while held before has
    /// no carried estimate and reads as an unknown entry.
    /// </summary>
    public static Dictionary<HolderSecurityKey, HolderCostSlot> Advance(
        IReadOnlyList<HolderEquityPosition> current,
        IReadOnlyList<HolderEquityPosition> previous,
        IReadOnlyDictionary<HolderSecurityKey, HolderCostSlot> previousSlots,
        IReadOnlyDictionary<HolderSecurityKey, decimal?> previousShareFactors,
        IReadOnlySet<HolderSecurityKey> unresolvedBasis,
        IReadOnlyDictionary<HolderSecurityKey, decimal> averagePrices
    )
    {
        var now = Books(current);
        var slots = new Dictionary<HolderSecurityKey, HolderCostSlot>(now.Count);
        if (previous == null)
        {
            foreach (var security in now.Keys)
                slots[security] = HolderCostSlot.UnknownEntry;
            return slots;
        }

        var before = Books(previous);
        foreach (var (security, book) in now)
        {
            var price = averagePrices.TryGetValue(security, out var average) ? average : 0m;
            if (!before.TryGetValue(security, out var prior))
            {
                slots[security] = Costs(book, price)
                    ? HolderCostSlot.Estimated(price)
                    : HolderCostSlot.Unpriced;
                continue;
            }
            if (unresolvedBasis.Contains(security))
            {
                slots[security] = HolderCostSlot.Unpriced;
                continue;
            }
            var carried = previousSlots.GetValueOrDefault(security, HolderCostSlot.UnknownEntry);
            if (carried.Status != HolderCostStatus.Estimated)
            {
                slots[security] = carried;
                continue;
            }
            var factor = previousShareFactors.TryGetValue(security, out var known)
                ? known.GetValueOrDefault(1m)
                : 1m;
            var cost = factor > 0 ? carried.CostPerShare.GetValueOrDefault() / factor : 0m;
            var bought = book.Shares - prior.Shares;
            if (cost <= 0)
                slots[security] = HolderCostSlot.UnknownEntry;
            else if (bought <= 0)
                slots[security] = HolderCostSlot.Estimated(cost);
            else if (!Costs(book, price) || !Costs(prior, price))
                slots[security] = HolderCostSlot.Unpriced;
            else
                slots[security] = HolderCostSlot.Estimated(
                    (prior.Shares * cost + bought * price) / book.Shares
                );
        }
        return slots;
    }

    // A buy is costed only at a positive average price whose shares stay within the cap of the
    // value the position reports; past it the shares are on another unit or basis.
    private static bool Costs(Book book, decimal price) =>
        price > 0
        && book.Reported is { } reported
        && !HoldingValueDisagreement.GrosslyExceeds(book.Shares * price, reported);

    private static Dictionary<HolderSecurityKey, Book> Books(
        IReadOnlyList<HolderEquityPosition> positions
    ) =>
        positions
            .Where(position => position.Shares > 0)
            .GroupBy(position => position.Security)
            .ToDictionary(
                group => group.Key,
                group => new Book(
                    group.Sum(position => position.Shares),
                    group.All(position => position.ReportedValue != null)
                        ? group.Sum(position => position.ReportedValue.GetValueOrDefault())
                        : null
                )
            );

    private sealed record Book(long Shares, long? Reported);
}
