using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Concentration of a filer's equity book by issuer: sibling share classes of one issuer are one
/// exposure, and unvalued positions count as stocks but carry no weight.
/// </summary>
public static class HolderConcentrationCalculator
{
    public const int TopPositions = 10;

    public static HolderConcentration Measure(IReadOnlyList<HolderEquityPosition> positions)
    {
        var held = positions.Where(position => position.Shares > 0).ToList();
        var stockCount = held.Select(position => position.Security.IssuerId).Distinct().Count();
        var weights = IssuerWeights(held);
        if (weights.Count == 0)
            return new HolderConcentration { StockCount = stockCount };

        var herfindahl = weights.Values.Sum(weight => weight * weight);
        return new HolderConcentration
        {
            StockCount = stockCount,
            TopTenWeight = weights.Values.OrderDescending().Take(TopPositions).Sum(),
            Herfindahl = herfindahl,
            EffectivePositions = 1m / herfindahl,
        };
    }

    /// <summary>Each issuer's share of the book's valued positions; empty when none is valued.</summary>
    public static Dictionary<Guid, decimal> IssuerWeights(
        IReadOnlyList<HolderEquityPosition> positions
    )
    {
        var values = IssuerValues(positions);
        var total = values.Values.Sum();
        return total > 0 ? values.ToDictionary(pair => pair.Key, pair => pair.Value / total) : [];
    }

    public static Dictionary<Guid, decimal> IssuerValues(
        IReadOnlyList<HolderEquityPosition> positions
    ) =>
        positions
            .Where(position => position is { Shares: > 0, Value: > 0 })
            .GroupBy(position => position.Security.IssuerId)
            .ToDictionary(group => group.Key, group => (decimal)group.Sum(p => p.Value.Value));
}
