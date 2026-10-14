using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// The picks every single-concept fact series shares. A flow is identified by its fiscal period, so
/// a series holds one fact per <c>(FiscalYear, FiscalPeriod)</c> bucket; a balance or a share count
/// is identified by its as-of date, so it holds one fact per date (see
/// <see cref="FinancialConceptAliases.IsInstant"/>).
/// </summary>
public static class FinancialFactSeriesShaper
{
    /// <summary>
    /// One fact for a fiscal bucket, or null when no fact measures the bucket's granularity.
    /// <paramref name="corroborationContext"/> is the alias's whole history, which a FullYear
    /// transition stub needs; without it such a stub fails closed.
    /// </summary>
    public static FinancialFact PickBestFact(
        IEnumerable<FinancialFact> bucket,
        IReadOnlyDictionary<Guid, int> conceptPriority,
        bool asOriginallyReported = false,
        IReadOnlyCollection<FinancialFact> corroborationContext = null
    )
    {
        // A quarter reads as the discrete three months, never the year-to-date total a 10-Q tags
        // under the same fiscal Q2/Q3. A duration over the annual ceiling is never a period.
        var candidates = bucket
            .Where(f =>
                f.PeriodType != FactPeriodType.Duration
                || Span(f) is >= 0 and <= FiscalPeriodSpanDays.MaxAnnual
            )
            .ToList();
        if (candidates.Count == 0)
            return null;

        var fiscalPeriod = candidates[0].FiscalPeriod;
        var preferredSpan = candidates
            .Where(f =>
                f.PeriodType != FactPeriodType.Duration
                || (
                    fiscalPeriod == SecFiscalPeriod.FullYear
                        ? Span(f) >= FiscalPeriodSpanDays.MinAnnual
                        : Span(f) <= FiscalPeriodSpanDays.MaxQuarter
                )
            )
            .ToList();
        if (preferredSpan.Count > 0)
        {
            // A period a duration measures is never read off an instant in the same bucket: the
            // instant is a declaration or payment-date disclosure dated after the period end.
            var durations = preferredSpan
                .Where(f => f.PeriodType == FactPeriodType.Duration)
                .ToList();
            candidates = durations.Count > 0 ? durations : preferredSpan;
        }
        else if (fiscalPeriod == SecFiscalPeriod.FullYear)
        {
            // FullYear is the enum's zero value, so unclassified sub-annual spans land here too;
            // only a transition stub its neighbouring years seam in on both sides publishes.
            candidates = candidates
                .Where(f => AnnualTransitionStubs.IsCorroborated(f, corroborationContext))
                .ToList();
            if (candidates.Count == 0)
                return null;
        }
        else
        {
            return null;
        }

        // Latest period end first: a filing re-reports comparative prior periods under its own
        // fiscal identity, and those tie the current figure on span and filed date.
        return OrderByAuthority(
                candidates.OrderByDescending(f => f.PeriodEnd),
                conceptPriority,
                asOriginallyReported
            )
            .First();
    }

    /// <summary>
    /// One fact per as-of date, for a series of balances or share counts. The value is the most
    /// authoritative filing of that date. The fiscal stamp is the smallest one the date carries, its
    /// own period, should a filing re-report the date under its own identity.
    /// </summary>
    public static List<FinancialFact> PickPerDate(
        IEnumerable<FinancialFact> facts,
        IReadOnlyDictionary<Guid, int> conceptPriority,
        bool asOriginallyReported = false
    ) =>
        facts
            .Where(f => f.PeriodStart == f.PeriodEnd)
            .GroupBy(f => f.PeriodEnd)
            .Select(date =>
            {
                var value = OrderByAuthority(
                        date.OrderBy(_ => 0),
                        conceptPriority,
                        asOriginallyReported
                    )
                    .First();
                var own = date.OrderBy(f => f.FiscalYear).ThenBy(f => f.FiscalPeriod).First();
                return value.FiscalYear == own.FiscalYear && value.FiscalPeriod == own.FiscalPeriod
                    ? value
                    : value.Restamped(own.FiscalYear, own.FiscalPeriod);
            })
            .ToList();

    /// <summary>
    /// One pick per actual reporting span. A date window can reduce a fiscal bucket to a comparative
    /// re-report of an earlier period; the smallest stamp is that period's own identity.
    /// </summary>
    public static List<FinancialFact> DedupeByReportingSpan(IEnumerable<FinancialFact> picked) =>
        picked
            .GroupBy(f => (f.PeriodStart, f.PeriodEnd, f.PeriodType))
            .Select(g => g.OrderBy(f => f.FiscalYear).ThenBy(f => f.FiscalPeriod).First())
            .ToList();

    // Canonical periodic source, then the alias's primary tag, then the latest filing (or the
    // earliest, as originally reported), with the accession as the stable same-day tiebreak.
    private static IOrderedEnumerable<FinancialFact> OrderByAuthority(
        IOrderedEnumerable<FinancialFact> facts,
        IReadOnlyDictionary<Guid, int> conceptPriority,
        bool asOriginallyReported
    )
    {
        var ranked = facts
            .ThenBy(f => FinancialFactSourcePriority.Rank(f.Form))
            .ThenBy(f => conceptPriority.GetValueOrDefault(f.FinancialConceptId, int.MaxValue));
        return asOriginallyReported
            ? ranked.ThenBy(f => f.FiledDate).ThenBy(f => f.AccessionNumber)
            : ranked.ThenByDescending(f => f.FiledDate).ThenByDescending(f => f.AccessionNumber);
    }

    private static int Span(FinancialFact fact) =>
        fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;
}
