namespace Equibles.Yahoo.Data.Prices;

// Whether a retired listing's late-written bars are a copy of the series another issuer's listing
// holds under the same symbol. Yahoo serves a bare symbol as its current holder's series, so a
// copy reproduces that holder's sessions, either as stored or on a basis that differs by the
// holder's own recorded splits. Single sessions prove nothing: flat SPAC and ETF bars at a round
// price coincide across issuers on any given day. Only a whole series counts: nearly every
// overlapping session agrees to the tick at one factor, and enough of them actually traded
// (high above low) that the agreement cannot be a run of flat prints.
public static class CopiedBarEvidence
{
    // Stored prices carry four decimals, so a copy agrees to one last-digit tick.
    public const decimal Tick = 0.0001m;

    // Share of the overlapping sessions that must agree at the one factor.
    public const decimal MinCopiedShare = 0.95m;

    // Agreeing sessions whose high differs from their low, so flat prints cannot carry the proof.
    public const int MinTradedCopies = 20;

    // Bounds the factor set: contiguous runs of a long split history only.
    private const int MaxSplitsConsidered = 24;

    public readonly record struct Bar(
        DateOnly Date,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close
    );

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

    // The dates of the retired listing's bars proven to copy one holder series, or none. Every
    // overlapping date goes once the series is proven, since one fetch wrote them all; a session
    // the holder later revised by more than a tick is still that fetch's copy.
    public static IReadOnlySet<DateOnly> CopiedDates(
        IReadOnlyCollection<Bar> own,
        IReadOnlyCollection<Bar> holder,
        IReadOnlyList<decimal> factors
    )
    {
        var holderByDate = holder
            .GroupBy(bar => bar.Date)
            .ToDictionary(group => group.Key, group => group.First());
        var overlap = own.Where(bar => holderByDate.ContainsKey(bar.Date)).ToList();
        if (overlap.Count == 0)
            return new HashSet<DateOnly>();

        foreach (var factor in factors)
        {
            var copies = overlap
                .Where(bar => Matches(bar, holderByDate[bar.Date], factor))
                .ToList();
            if (
                copies.Count >= MinCopiedShare * overlap.Count
                && copies.Count(bar => bar.High != bar.Low) >= MinTradedCopies
            )
                return overlap.Select(bar => bar.Date).ToHashSet();
        }
        return new HashSet<DateOnly>();
    }

    private static bool Matches(Bar own, Bar holder, decimal factor) =>
        Agrees(own.Open, holder.Open * factor)
        && Agrees(own.High, holder.High * factor)
        && Agrees(own.Low, holder.Low * factor)
        && Agrees(own.Close, holder.Close * factor);

    private static bool Agrees(decimal own, decimal holder) => Math.Abs(own - holder) <= Tick;
}
