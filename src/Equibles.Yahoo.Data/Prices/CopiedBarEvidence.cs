namespace Equibles.Yahoo.Data.Prices;

// Whether a retired listing's stored bar is a copy of the series another issuer's listing holds
// under the same symbol. Yahoo serves a bare symbol as its current holder's series, so a copy is
// that holder's session on the same date, either as stored or on a basis that differs by the
// holder's own recorded splits. Two different companies' sessions do not agree on all four prices
// at one of those factors, so a listing's own history never matches.
public static class CopiedBarEvidence
{
    // Bounds the factor set: contiguous runs of a long split history only.
    private const int MaxSplitsConsidered = 24;

    public readonly record struct Ohlc(decimal Open, decimal High, decimal Low, decimal Close);

    // Every basis on which the holder's stored bars and a copy can differ: no split, or any
    // contiguous run of the holder's splits applied in either direction.
    public static IReadOnlyList<decimal> BasisFactors(
        IEnumerable<(DateOnly EffectiveDate, decimal Numerator, decimal Denominator)> splits
    )
    {
        var ratios = splits
            .Where(split => split.Numerator > 0m && split.Denominator > 0m)
            .OrderBy(split => split.EffectiveDate)
            .TakeLast(MaxSplitsConsidered)
            .Select(split => split.Numerator / split.Denominator)
            .ToList();
        var factors = new HashSet<decimal> { 1m };
        for (var start = 0; start < ratios.Count; start++)
        {
            var product = 1m;
            for (var end = start; end < ratios.Count; end++)
            {
                product *= ratios[end];
                factors.Add(product);
                factors.Add(1m / product);
            }
        }
        return factors.ToList();
    }

    public static bool IsCopy(
        Ohlc own,
        IEnumerable<Ohlc> holderSessions,
        IReadOnlyList<decimal> factors
    ) => holderSessions.Any(holder => factors.Any(factor => Matches(own, holder, factor)));

    private static bool Matches(Ohlc own, Ohlc holder, decimal factor) =>
        Agrees(own.Open, holder.Open * factor)
        && Agrees(own.High, holder.High * factor)
        && Agrees(own.Low, holder.Low * factor)
        && Agrees(own.Close, holder.Close * factor);

    private static bool Agrees(decimal own, decimal holder) =>
        own == holder || DailyBarGuards.IsSameSplitBasis(own, holder);
}
