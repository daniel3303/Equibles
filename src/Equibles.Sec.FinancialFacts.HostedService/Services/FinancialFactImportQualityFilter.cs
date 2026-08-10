using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Rejects Company Facts rows whose source is demonstrably lower-quality than another row for
/// the same actual period. Rejected natural keys are also deleted during a versioned replay so a
/// bad value already in the store cannot remain selectable.
/// </summary>
internal static class FinancialFactImportQualityFilter
{
    private const int MinAnnualDays = 350;
    private const int MaxAnnualDays = 380;
    private const int MinQuarterDays = 80;
    private const int MaxQuarterDays = 100;
    private const int MinimumQuarterCount = 3;
    private const decimal ScaleMatchTolerance = 0.05m;
    private const decimal MinimumScaledQuarterRatio = 0.1m;
    private const decimal MaximumScaledQuarterRatio = 10m;

    private static readonly decimal[] SuspectScaleFactors = [1_000m, 1_000_000m];

    internal static FilterResult Apply(IReadOnlyCollection<FinancialFact> facts)
    {
        var rejected = new HashSet<FinancialFact>();

        foreach (
            var series in facts.GroupBy(f =>
                (f.CommonStockId, f.FinancialConceptId, f.Unit, f.DimensionsKey)
            )
        )
        {
            var rows = series.ToList();
            foreach (var candidate in rows.Where(IsAnnualDuration))
            {
                if (HasCorroboratedScaleError(candidate, rows))
                    rejected.Add(candidate);
            }
        }

        var scaleClean = facts.Where(f => !rejected.Contains(f)).ToList();
        foreach (
            var period in scaleClean.GroupBy(f =>
                (
                    f.CommonStockId,
                    f.FinancialConceptId,
                    f.Unit,
                    f.PeriodType,
                    f.PeriodStart,
                    f.PeriodEnd,
                    f.DimensionsKey
                )
            )
        )
        {
            if (!period.Any(f => FinancialFactSourcePriority.Rank(f.Form) == 0))
                continue;

            foreach (var proxy in period.Where(f => f.Form == DocumentType.Def14A))
                rejected.Add(proxy);
        }

        return new FilterResult(
            facts.Where(f => !rejected.Contains(f)).ToList(),
            facts.Where(rejected.Contains).ToList()
        );
    }

    private static bool HasCorroboratedScaleError(
        FinancialFact candidate,
        IReadOnlyCollection<FinancialFact> series
    )
    {
        if (!LooksLikeCurrency(candidate.Unit) || candidate.Value == 0)
            return false;

        var earlierAnnuals = series
            .Where(f =>
                f != candidate
                && IsAnnualDuration(f)
                && f.PeriodStart == candidate.PeriodStart
                && f.PeriodEnd == candidate.PeriodEnd
                && f.FiledDate < candidate.FiledDate
                && f.AccessionNumber != candidate.AccessionNumber
                && FinancialFactSourcePriority.Rank(f.Form) == 0
                && f.Value != 0
            )
            .OrderByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.AccessionNumber)
            .ToList();
        if (earlierAnnuals.Count == 0)
            return false;

        var quarterRows = series
            .Where(f =>
                f.PeriodType == FactPeriodType.Duration
                && f.PeriodStart >= candidate.PeriodStart
                && f.PeriodEnd <= candidate.PeriodEnd
                && SpanDays(f) >= MinQuarterDays
                && SpanDays(f) <= MaxQuarterDays
            )
            .GroupBy(f => (f.PeriodStart, f.PeriodEnd))
            .Select(g =>
                g.OrderBy(f => FinancialFactSourcePriority.Rank(f.Form))
                    .ThenByDescending(f => f.FiledDate)
                    .ThenByDescending(f => f.AccessionNumber)
                    .First()
            )
            .ToList();
        if (quarterRows.Count < MinimumQuarterCount)
            return false;

        var quarterSum = SaturatingSum(quarterRows.Select(f => f.Value));
        var quarterMagnitude = SaturatingSum(quarterRows.Select(f => Magnitude(f.Value)));
        if (
            quarterMagnitude == 0
            || quarterSum == 0
            || Math.Sign(quarterSum) != Math.Sign(candidate.Value)
        )
            return false;

        foreach (var reference in earlierAnnuals)
        {
            if (Math.Sign(reference.Value) != Math.Sign(candidate.Value))
                continue;

            foreach (var scaleFactor in SuspectScaleFactors)
            {
                var scaledCandidate = candidate.Value / scaleFactor;
                if (!ApproximatelyEqual(scaledCandidate, reference.Value))
                    continue;

                var scaledQuarterRatio = Magnitude(scaledCandidate) / quarterMagnitude;
                if (
                    scaledQuarterRatio >= MinimumScaledQuarterRatio
                    && scaledQuarterRatio <= MaximumScaledQuarterRatio
                )
                    return true;
            }
        }

        return false;
    }

    private static bool IsAnnualDuration(FinancialFact fact) =>
        fact.PeriodType == FactPeriodType.Duration
        && SpanDays(fact) >= MinAnnualDays
        && SpanDays(fact) <= MaxAnnualDays;

    private static int SpanDays(FinancialFact fact) =>
        fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;

    private static bool LooksLikeCurrency(string unit) =>
        unit is { Length: 3 } && unit.All(char.IsAsciiLetterUpper);

    private static bool ApproximatelyEqual(decimal left, decimal right)
    {
        var denominator = Math.Max(Magnitude(left), Magnitude(right));
        return denominator > 0 && Magnitude(left - right) / denominator <= ScaleMatchTolerance;
    }

    private static decimal Magnitude(decimal value) =>
        value == decimal.MinValue ? decimal.MaxValue : Math.Abs(value);

    private static decimal SaturatingSum(IEnumerable<decimal> values)
    {
        var result = 0m;
        foreach (var value in values)
        {
            if (value > 0 && result > decimal.MaxValue - value)
                return decimal.MaxValue;
            if (value < 0 && result < decimal.MinValue - value)
                return decimal.MinValue;
            result += value;
        }
        return result;
    }

    internal sealed record FilterResult(
        IReadOnlyList<FinancialFact> Accepted,
        IReadOnlyList<FinancialFact> Rejected
    );
}
