using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Detects and repairs 13F positions filed on the basis of a split that took effect between the
/// quarter's end and the filing. Every count is restated from its report date, so a count the
/// filer already moved to the later basis takes the split twice, and a value the filer marked at
/// the later basis's price publishes a fraction of the position.
/// </summary>
/// <remarks>
/// Both errors leave one trace: the count times the quarter's price on the report-date basis
/// disagrees with the filed value by the straddled split's ratio. Only the holder's neighbouring
/// quarter tells them apart, a neighbour near the filed count meaning the value was marked late.
/// With no neighbour the count is taken as late, the more common of the two errors. The quarter's
/// other positions are the control: a split stamped as price-adjusted before its closes moved puts
/// every consistent position off by the same ratio, and rebasing those would rewrite filed figures
/// that no later price fix undoes.
/// </remarks>
internal static class SplitBasisShareCountRepairer
{
    internal enum Kind
    {
        PostSplitShares,
        PostSplitValue,
    }

    /// <summary>
    /// The ratio of the splits effective after the report date and up to the filing date, or null
    /// when either basis is unresolved or the ratio is too close to one to tell the bases apart.
    /// </summary>
    internal static decimal? StraddledFactor(
        DateOnly reportDate,
        DateOnly filingDate,
        Func<DateOnly, decimal?> shareCountFactorAt
    )
    {
        if (filingDate <= reportDate)
            return null;
        if (shareCountFactorAt(reportDate) is not { } atReport || atReport <= 0)
            return null;
        if (shareCountFactorAt(filingDate) is not { } atFiling || atFiling <= 0)
            return null;
        var factor = atReport / atFiling;
        return Agrees(factor, 1m) ? null : factor;
    }

    /// <summary>
    /// True for a share position this repair may judge: a priced quantity with the filer's own
    /// figure to compare. Principal amounts and option legs never carry a share basis.
    /// </summary>
    internal static bool MayClassify(InstitutionalHolding holding) =>
        holding.ShareType == ShareType.Shares && holding.OptionType == null && holding.Shares > 0;

    /// <summary>
    /// A position's summed count and filed value across its legs, or null when either leaves the
    /// range of a stored figure (both are filer-written).
    /// </summary>
    internal static (long Shares, long FiledValue)? PositionTotals(
        IEnumerable<InstitutionalHolding> legs
    )
    {
        var shares = 0m;
        var filedValue = 0m;
        foreach (var leg in legs)
        {
            shares += leg.Shares;
            filedValue += leg.FiledValue ?? 0L;
        }
        return shares <= long.MaxValue && filedValue <= long.MaxValue
            ? ((long)shares, (long)filedValue)
            : null;
    }

    // Fewer consistent positions than this cannot vouch for the quarter's price basis.
    internal const int MinCorroboratingPositions = 3;

    /// <summary>
    /// True when the quarter's positions in the listing confirm its price basis: at least
    /// <see cref="MinCorroboratingPositions"/> fit the price as filed, and they outnumber those off
    /// by the split's ratio. A later-basis filer is a minority of the quarter's holders; a price
    /// series still on the as-traded basis puts nearly every holder off by the ratio.
    /// </summary>
    internal static bool PriceBasisCorroborated(
        IEnumerable<(long Shares, long FiledValue)> quarter,
        decimal filingBasisPrice,
        decimal factor
    )
    {
        var consistent = 0;
        var offByRatio = 0;
        foreach (var (shares, filedValue) in quarter)
        {
            if (shares <= 0 || filedValue <= 0)
                continue;
            var ratio = shares * filingBasisPrice / filedValue;
            if (Agrees(ratio, 1m))
                consistent++;
            else if (Agrees(ratio, factor))
                offByRatio++;
        }
        return consistent >= MinCorroboratingPositions && consistent > offByRatio;
    }

    /// <summary>
    /// Which figure the filer moved to the later basis, or null when the position is consistent
    /// on its report date. <paramref name="neighbourShares"/> is the holder's nearest other
    /// quarter, already restated onto this position's report-date basis.
    /// </summary>
    internal static Kind? Classify(
        long shares,
        long filedValue,
        decimal filingBasisPrice,
        decimal factor,
        long? neighbourShares
    )
    {
        if (shares <= 0 || filedValue <= 0 || filingBasisPrice <= 0 || factor <= 0)
            return null;
        var ratio = shares * filingBasisPrice / filedValue;
        if (!Agrees(ratio, factor) || Agrees(ratio, 1m))
            return null;
        if (shares / factor > long.MaxValue || filedValue * factor > long.MaxValue)
            return null;
        if (neighbourShares is not > 0)
            return Kind.PostSplitShares;

        var asFiledDistance = Math.Abs(Math.Log((double)neighbourShares.Value / shares));
        var rebasedDistance = Math.Abs(
            Math.Log((double)neighbourShares.Value * (double)factor / shares)
        );
        return rebasedDistance < asFiledDistance ? Kind.PostSplitShares : Kind.PostSplitValue;
    }

    // Only a quarter this close tells a late count from a late value; further away the position
    // itself has moved too much to say.
    internal const int NeighbourWindowDays = 120;

    /// <summary>
    /// The holder's nearest other quarter of a position, earlier first, restated onto the
    /// position's report-date basis; null when no quarter is close enough or a basis is unresolved.
    /// </summary>
    internal static long? NearestNeighbour(
        DateOnly reportDate,
        IEnumerable<(DateOnly ReportDate, long Shares)> otherQuarters,
        Func<DateOnly, decimal?> shareCountFactorAt
    )
    {
        var quarters = otherQuarters
            .Where(quarter =>
                quarter.ReportDate != reportDate
                && Math.Abs(quarter.ReportDate.DayNumber - reportDate.DayNumber)
                    <= NeighbourWindowDays
            )
            .GroupBy(quarter => quarter.ReportDate)
            .Select(quarter =>
                (ReportDate: quarter.Key, Shares: quarter.Sum(q => (decimal)q.Shares))
            )
            .ToList();
        var earlier = quarters.Where(quarter => quarter.ReportDate < reportDate).ToList();
        var later = quarters.Where(quarter => quarter.ReportDate > reportDate).ToList();
        if (earlier.Count == 0 && later.Count == 0)
            return null;
        var neighbour =
            earlier.Count > 0
                ? earlier.MaxBy(quarter => quarter.ReportDate)
                : later.MinBy(quarter => quarter.ReportDate);
        if (
            shareCountFactorAt(neighbour.ReportDate) is not { } neighbourFactor
            || shareCountFactorAt(reportDate) is not { } positionFactor
            || positionFactor <= 0
        )
            return null;
        var restated = Math.Round(neighbour.Shares * neighbourFactor / positionFactor);
        return restated <= long.MaxValue ? (long)restated : null;
    }

    /// <summary>A count moved back from the later basis onto the report date's.</summary>
    internal static long Rebase(long count, decimal factor) => Saturate(count / factor);

    /// <summary>A value marked at the later basis's price moved back onto the report date's.</summary>
    internal static long Revalue(long value, decimal factor) => Saturate(value * factor);

    // Voting columns are filer-written and never range-checked, so a rebase past a stored figure's
    // range keeps the bound rather than failing the whole filing.
    private static long Saturate(decimal value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return rounded >= long.MaxValue ? long.MaxValue
            : rounded <= long.MinValue ? long.MinValue
            : (long)rounded;
    }

    /// <summary>
    /// Repairs every position of one filing whose figures sit on a straddled split's later basis
    /// and whose quarter <paramref name="inputs"/> names as corroborated. A position's legs are
    /// judged together, since the neighbour is the whole position; <paramref name="revalue"/>
    /// re-derives each repaired leg. Returns one correction per repaired position.
    /// </summary>
    internal static List<SplitBasisCorrection> Repair(
        List<BufferedHoldingRow> rows,
        Func<InstitutionalHolding, string> positionKey,
        Func<InstitutionalHolding, decimal?> straddledFactor,
        Func<InstitutionalHolding, decimal?> filingBasisPrice,
        SplitBasisInputs inputs,
        Action<BufferedHoldingRow> revalue
    )
    {
        var corrections = new List<SplitBasisCorrection>();
        foreach (
            var position in rows.Where(row => MayClassify(row.Holding))
                .GroupBy(row => positionKey(row.Holding))
        )
        {
            if (!inputs.CorroboratedPositions.Contains(position.Key))
                continue;
            var legs = position.ToList();
            var first = legs[0].Holding;
            if (
                straddledFactor(first) is not { } factor
                || filingBasisPrice(first) is not { } price
                || PositionTotals(legs.Select(leg => leg.Holding))
                    is not
                    (var shares, var filedValue)
            )
                continue;
            long? neighbour = inputs.Neighbours.TryGetValue(position.Key, out var found)
                ? found
                : null;
            if (Classify(shares, filedValue, price, factor, neighbour) is not { } kind)
                continue;

            foreach (var leg in legs)
            {
                var holding = leg.Holding;
                if (kind == Kind.PostSplitShares)
                {
                    holding.Shares = Rebase(holding.Shares, factor);
                    holding.VotingAuthSole = Rebase(holding.VotingAuthSole, factor);
                    holding.VotingAuthShared = Rebase(holding.VotingAuthShared, factor);
                    holding.VotingAuthNone = Rebase(holding.VotingAuthNone, factor);
                    leg.ManagerEntry.Shares = Rebase(leg.ManagerEntry.Shares, factor);
                }
                else if (holding.FiledValue is { } legValue)
                {
                    holding.FiledValue = Revalue(legValue, factor);
                }
                revalue(leg);
            }

            corrections.Add(
                new SplitBasisCorrection(
                    kind,
                    factor,
                    first.AccessionNumber,
                    first.Cusip,
                    first.ListedTicker,
                    first.ReportDate,
                    first.FilingDate,
                    shares,
                    filedValue,
                    neighbour
                )
            );
        }
        return corrections;
    }

    // The band within which a filer's own mark and the stored close are the same price.
    private static bool Agrees(decimal ratio, decimal expected)
    {
        var relative = ratio / expected;
        return relative <= QuarterAveragePriceCalculator.MaximumFiledPriceRatio
            && relative * QuarterAveragePriceCalculator.MaximumFiledPriceRatio >= 1;
    }
}
