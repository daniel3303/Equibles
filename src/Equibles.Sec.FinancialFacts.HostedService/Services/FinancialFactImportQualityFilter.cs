using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Rejects Company Facts rows whose source is demonstrably lower-quality than another row for
/// the same actual period. Rejected natural keys are also deleted during a versioned replay so a
/// bad value already in the store cannot remain selectable.
/// </summary>
/// <remarks>
/// The lower-priority rule is keyed on the source RANK, not on a named form. It began as a
/// DEF 14A rule, and the form that broke it was a PRE 14A: Company Facts states the form name
/// verbatim, an unmapped name becomes <see cref="DocumentType.Other" />, and both land at the
/// same lowest rank while only one was being rejected. RealReal's preliminary proxy restated
/// five fiscal years of NetIncomeLoss with the sign flipped and a thousand-fold scale
/// (FY2025: +41,799,000,000 against the 10-K's -41,799,000), which the scale rule cannot catch
/// because it requires the candidate and the quarter sum to share a sign. Anything a periodic
/// report already states for the exact same period adds nothing, so rank decides.
/// </remarks>
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

    // FinancialFactSourcePriority: periodic reports rank 0, 8-K/6-K rank 1, everything else
    // (proxies, registration statements, prospectuses, and any form name Company Facts states
    // that DocumentType does not map) ranks last.
    private const int LowestSourceRank = 2;

    private static readonly decimal[] SuspectScaleFactors = [1_000m, 1_000_000m];

    internal static FilterResult Apply(
        IReadOnlyCollection<FinancialFact> facts,
        IReadOnlyDictionary<(FactTaxonomy Taxonomy, string Tag), Guid> conceptIds
    )
    {
        var rejected = new HashSet<FinancialFact>();
        var aliasFamilies = BuildAliasFamilies(conceptIds);

        foreach (
            var series in facts.GroupBy(f =>
                (
                    f.CommonStockId,
                    ConceptFamily(f.FinancialConceptId, aliasFamilies),
                    f.Unit,
                    f.DimensionsKey
                )
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

            foreach (
                var restatement in period.Where(f =>
                    FinancialFactSourcePriority.Rank(f.Form) == LowestSourceRank
                )
            )
                rejected.Add(restatement);
        }

        return new FilterResult(
            facts.Where(f => !rejected.Contains(f)).ToList(),
            facts.Where(rejected.Contains).ToList()
        );
    }

    private static IReadOnlyDictionary<Guid, Guid> BuildAliasFamilies(
        IReadOnlyDictionary<(FactTaxonomy Taxonomy, string Tag), Guid> conceptIds
    )
    {
        var neighbours = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var alias in FinancialConceptAliases.SupportedAliases)
        {
            if (!FinancialConceptAliases.TryResolve(alias, out var concepts))
                continue;

            var ids = concepts
                .Select(c => conceptIds.GetValueOrDefault((c.Taxonomy, c.Tag)))
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (ids.Count < 2)
                continue;

            var first = ids[0];
            foreach (var id in ids)
            {
                neighbours.TryAdd(id, []);
                neighbours[id].Add(first);
                neighbours[first].Add(id);
            }
        }

        var familyByConcept = new Dictionary<Guid, Guid>();
        foreach (var conceptId in neighbours.Keys)
        {
            if (familyByConcept.ContainsKey(conceptId))
                continue;

            var component = new List<Guid>();
            var pending = new Stack<Guid>();
            pending.Push(conceptId);
            while (pending.TryPop(out var current))
            {
                if (familyByConcept.ContainsKey(current))
                    continue;

                familyByConcept[current] = Guid.Empty;
                component.Add(current);
                foreach (var neighbour in neighbours[current])
                    pending.Push(neighbour);
            }

            var familyId = component.Min();
            foreach (var member in component)
                familyByConcept[member] = familyId;
        }

        return familyByConcept;
    }

    private static Guid ConceptFamily(
        Guid conceptId,
        IReadOnlyDictionary<Guid, Guid> aliasFamilies
    ) => aliasFamilies.GetValueOrDefault(conceptId, conceptId);

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
            .OrderBy(f => f.FinancialConceptId == candidate.FinancialConceptId ? 0 : 1)
            .ThenByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.AccessionNumber)
            .ThenBy(f => f.FinancialConceptId)
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
                g.OrderBy(f => f.FinancialConceptId == candidate.FinancialConceptId ? 0 : 1)
                    .ThenBy(f => FinancialFactSourcePriority.Rank(f.Form))
                    .ThenByDescending(f => f.FiledDate)
                    .ThenByDescending(f => f.AccessionNumber)
                    .ThenBy(f => f.FinancialConceptId)
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
