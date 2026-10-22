using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// <c>OperatingCostsAndExpenses</c> is its own line because filers use it both with and without
/// cost of revenue. One filer's use is provable, though: where it states the same figure as its
/// operating-expenses (or total costs and expenses) line in every period both cover, it is that
/// line under another tag. EBS tagged OperatingExpenses through FY2019 and OperatingCostsAndExpenses
/// from FY2018, equal in FY2018, FY2019 and FY2019 Q1–Q3; the claimed line then continues through
/// the newer tag and the duplicate line is not shown.
/// </summary>
public static class OperatingCostsContinuation
{
    public const string OperatingCostsAlias = "operating-costs-and-expenses";
    public const string OperatingExpensesAlias = "operating-expenses";
    public const string TotalCostsAlias = "total-costs-and-expenses";

    // As for a member rename: one shared figure can be chance, two in different periods cannot.
    private const int MinOverlaps = 2;

    /// <summary>The claimable alias <paramref name="concepts"/> is, or null.</summary>
    public static string ClaimableAlias(IReadOnlyList<FinancialConceptAliases.ConceptRef> concepts)
    {
        foreach (var alias in new[] { OperatingExpensesAlias, TotalCostsAlias })
        {
            if (
                FinancialConceptAliases.TryResolve(alias, out var resolved)
                && ReferenceEquals(resolved, concepts)
            )
                return alias;
        }
        return null;
    }

    /// <summary>
    /// The line the filer's OperatingCostsAndExpenses continues, or null. Each concept is read at
    /// its reported (never derived) consolidated spans, the latest canonical filing per exact span
    /// and unit. A line claims it when every span both state carries the same positive figure, and
    /// they share two spans or the operating costs add none; operating expenses is tried first.
    /// </summary>
    public static string Claimant(
        IReadOnlyCollection<FinancialFact> operatingCosts,
        IReadOnlyCollection<FinancialFact> operatingExpenses,
        IReadOnlyCollection<FinancialFact> totalCosts
    )
    {
        var costs = ByPeriod(operatingCosts);
        if (costs.Count == 0)
            return null;
        if (Continues(costs, ByPeriod(operatingExpenses)))
            return OperatingExpensesAlias;
        if (Continues(costs, ByPeriod(totalCosts)))
            return TotalCostsAlias;
        return null;
    }

    /// <summary>The claimant over one issuer's facts for the three lines' concepts.</summary>
    public static string Claimant(
        IReadOnlyCollection<FinancialFact> facts,
        IReadOnlyDictionary<(FactTaxonomy Taxonomy, string Tag), Guid> conceptIdByKey
    )
    {
        List<FinancialFact> Of(string alias)
        {
            var ids = ConceptIds(alias, conceptIdByKey);
            return facts.Where(f => ids.Contains(f.FinancialConceptId)).ToList();
        }
        return Claimant(Of(OperatingCostsAlias), Of(OperatingExpensesAlias), Of(TotalCostsAlias));
    }

    /// <summary>The ingested concept ids of one of the three lines.</summary>
    public static HashSet<Guid> ConceptIds(
        string alias,
        IReadOnlyDictionary<(FactTaxonomy Taxonomy, string Tag), Guid> conceptIdByKey
    ) =>
        FinancialConceptAliases.TryResolve(alias, out var concepts)
            ? concepts
                .Select(c =>
                    conceptIdByKey.TryGetValue((c.Taxonomy, c.Tag), out var id) ? id : (Guid?)null
                )
                .OfType<Guid>()
                .ToHashSet()
            : [];

    /// <summary>
    /// The concepts every claim reads, for a caller loading an issuer's history to decide one.
    /// </summary>
    public static IEnumerable<FinancialConceptAliases.ConceptRef> ConceptRefs() =>
        new[] { OperatingCostsAlias, OperatingExpensesAlias, TotalCostsAlias }.SelectMany(alias =>
            FinancialConceptAliases.TryResolve(alias, out var concepts) ? concepts : []
        );

    /// <summary>
    /// The line's facts with the operating costs folded in when <paramref name="alias"/> is the
    /// claimant, ranked after every concept of the line so they only fill periods it lacks.
    /// </summary>
    public static (List<FinancialFact> Facts, Dictionary<Guid, int> Priority) Fold(
        string alias,
        string claimant,
        IReadOnlyCollection<FinancialFact> lineFacts,
        IReadOnlyDictionary<Guid, int> priority,
        IReadOnlyCollection<FinancialFact> operatingCosts
    )
    {
        var merged = new Dictionary<Guid, int>(priority);
        if (alias == null || alias != claimant || operatingCosts.Count == 0)
            return ([.. lineFacts], merged);

        var next = merged.Count == 0 ? 0 : merged.Values.Max() + 1;
        foreach (var conceptId in operatingCosts.Select(f => f.FinancialConceptId).Distinct())
            merged.TryAdd(conceptId, next);
        return ([.. lineFacts, .. operatingCosts], merged);
    }

    private static bool Continues(
        Dictionary<(DateOnly, DateOnly, string), decimal> costs,
        Dictionary<(DateOnly, DateOnly, string), decimal> line
    )
    {
        var overlaps = 0;
        foreach (var (period, value) in costs)
        {
            if (!line.TryGetValue(period, out var other))
                continue;
            if (value <= 0 || other <= 0 || !GrossProfitMistag.SameFigure(value, other))
                return false;
            overlaps++;
        }
        return overlaps >= MinOverlaps || (overlaps > 0 && overlaps == costs.Count);
    }

    private static Dictionary<(DateOnly, DateOnly, string), decimal> ByPeriod(
        IEnumerable<FinancialFact> facts
    ) =>
        facts
            .Where(f =>
                f.PeriodType == FactPeriodType.Duration
                && f.DimensionsKey == ""
                && !StatementQuarterDerivation.IsDerived(f)
                && f.PeriodEnd.DayNumber - f.PeriodStart.DayNumber
                    is > 0
                        and <= FiscalPeriodSpanDays.MaxAnnual
            )
            .GroupBy(f => (f.PeriodStart, f.PeriodEnd, f.Unit))
            .ToDictionary(
                g => g.Key,
                g =>
                    g.OrderBy(f => FinancialFactSourcePriority.Rank(f.Form))
                        .ThenByDescending(f => f.FiledDate)
                        .ThenByDescending(f => f.AccessionNumber, StringComparer.Ordinal)
                        .First()
                        .Value
            );
}
