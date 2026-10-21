using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// A bank's top line is net interest income plus noninterest income. Its <c>Revenues</c> tag can be
/// one slice of that (Zions FY2026 Q2: Revenues 182M against 677M net interest income plus 460M
/// noninterest income), and many banks tag no revenue concept at all, so the sum stands in for the
/// revenue alias wherever revenue is missing or clearly partial.
/// </summary>
public static class BankTopLine
{
    public const string RevenueAlias = "revenue";
    public const string NetInterestIncomeAlias = "net-interest-income";
    public const string NoninterestIncomeAlias = "noninterest-income";

    /// <summary>
    /// The concept id every summed fact carries. No stored concept has it, so a sum never collides
    /// with a component's own facts and its derived quarters subtract like any one concept.
    /// </summary>
    public static readonly Guid ConceptId = new("b4a1e7c2-3f5d-4e86-9a0b-6c2d8e1f7a53");

    // A reported top line within a tenth of the bank sum is the filer's own headline.
    private const decimal PartialTopLineMargin = 0.1m;

    /// <summary>
    /// True when <paramref name="concepts"/> is the revenue alias (or one of its synonyms, which
    /// resolve to the same list).
    /// </summary>
    public static bool IsRevenue(IReadOnlyList<FinancialConceptAliases.ConceptRef> concepts) =>
        FinancialConceptAliases.TryResolve(RevenueAlias, out var revenue)
        && ReferenceEquals(revenue, concepts);

    /// <summary>
    /// The revenue alias's facts with the bank sum folded in, and the pick priority that goes with
    /// them. A sum pairs the two components one filing states for one exact period. When the sum
    /// exceeds the newest revenue period by more than a tenth, revenue is a partial line and the
    /// sum outranks every revenue tag; otherwise it only fills periods revenue leaves empty.
    /// </summary>
    public static (List<FinancialFact> Facts, Dictionary<Guid, int> Priority) Fold(
        IReadOnlyCollection<FinancialFact> revenue,
        IReadOnlyCollection<FinancialFact> netInterestIncome,
        IReadOnlyCollection<FinancialFact> noninterestIncome,
        IReadOnlyDictionary<Guid, int> revenuePriority
    )
    {
        var priority = new Dictionary<Guid, int>(revenuePriority);
        var sums = Sums(netInterestIncome, noninterestIncome);
        if (sums.Count == 0)
            return ([.. revenue], priority);

        priority[ConceptId] = IsPartialAtNewest(revenue, sums, revenuePriority)
            ? int.MinValue
            : int.MaxValue;
        return ([.. revenue, .. sums], priority);
    }

    /// <summary>
    /// One period's top line, for a statement that shows a single period: the bank sum where the
    /// period has no revenue or its revenue is more than a tenth below the sum, else the revenue.
    /// The components may come from different filings, as each statement line takes its own latest
    /// restatement; a derived component makes the sum derived.
    /// </summary>
    public static FinancialFact PickForPeriod(
        FinancialFact revenue,
        FinancialFact netInterestIncome,
        FinancialFact noninterestIncome
    )
    {
        if (
            netInterestIncome == null
            || noninterestIncome == null
            || !IsConsolidatedDuration(netInterestIncome)
            || !IsConsolidatedDuration(noninterestIncome)
            || !SamePeriod(netInterestIncome, noninterestIncome)
        )
            return revenue;

        var sum = Sum(netInterestIncome, noninterestIncome);
        if (sum == null)
            return revenue;
        if (revenue == null)
            return sum;
        return SamePeriod(revenue, sum) && IsPartial(revenue, sum) ? sum : revenue;
    }

    private static List<FinancialFact> Sums(
        IReadOnlyCollection<FinancialFact> netInterestIncome,
        IReadOnlyCollection<FinancialFact> noninterestIncome
    )
    {
        var noninterestByFiling = noninterestIncome
            .Where(IsConsolidatedDuration)
            .GroupBy(f => (f.AccessionNumber, f.PeriodStart, f.PeriodEnd, f.Unit))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.FiledDate).First());
        return netInterestIncome
            .Where(IsConsolidatedDuration)
            .GroupBy(f => (f.AccessionNumber, f.PeriodStart, f.PeriodEnd, f.Unit))
            .Select(g =>
                noninterestByFiling.TryGetValue(g.Key, out var noninterest)
                    ? Sum(g.OrderByDescending(f => f.FiledDate).First(), noninterest)
                    : null
            )
            .OfType<FinancialFact>()
            .ToList();
    }

    // The newest revenue period as the alias reads it: the latest period end, its shortest span
    // (the discrete quarter beside the year-to-date), then the preferred tag and latest filing.
    private static bool IsPartialAtNewest(
        IReadOnlyCollection<FinancialFact> revenue,
        IReadOnlyCollection<FinancialFact> sums,
        IReadOnlyDictionary<Guid, int> revenuePriority
    )
    {
        var newest = revenue
            .Where(IsConsolidatedDuration)
            .OrderByDescending(f => f.PeriodEnd)
            .ThenBy(Span)
            .ThenBy(f => revenuePriority.GetValueOrDefault(f.FinancialConceptId, int.MaxValue))
            .ThenByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.AccessionNumber, StringComparer.Ordinal)
            .FirstOrDefault();
        if (newest == null)
            return false;

        var sum = sums.Where(s => SamePeriod(s, newest))
            .OrderByDescending(s => s.FiledDate)
            .ThenByDescending(s => s.AccessionNumber, StringComparer.Ordinal)
            .FirstOrDefault();
        return sum != null && IsPartial(newest, sum);
    }

    // A negative or zero sum is no top line; a copy keeps EF-tracked components untouched.
    private static FinancialFact Sum(
        FinancialFact netInterestIncome,
        FinancialFact noninterestIncome
    )
    {
        var value = netInterestIncome.Value + noninterestIncome.Value;
        if (value <= 0m)
            return null;

        var stating =
            netInterestIncome.FiledDate >= noninterestIncome.FiledDate
                ? netInterestIncome
                : noninterestIncome;
        var sum = stating.Restamped(netInterestIncome.FiscalYear, netInterestIncome.FiscalPeriod);
        sum.FinancialConceptId = ConceptId;
        sum.Value = value;
        if (
            StatementQuarterDerivation.IsDerived(netInterestIncome)
            || StatementQuarterDerivation.IsDerived(noninterestIncome)
        )
        {
            sum.Form = null;
            sum.AccessionNumber = null;
            sum.DocumentId = null;
        }
        return sum;
    }

    private static bool IsPartial(FinancialFact direct, FinancialFact topLine) =>
        topLine.Value - direct.Value > Math.Abs(direct.Value) * PartialTopLineMargin;

    private static bool SamePeriod(FinancialFact left, FinancialFact right) =>
        left.PeriodStart == right.PeriodStart
        && left.PeriodEnd == right.PeriodEnd
        && string.Equals(left.Unit, right.Unit, StringComparison.Ordinal);

    private static bool IsConsolidatedDuration(FinancialFact fact) =>
        fact.PeriodType == FactPeriodType.Duration
        && fact.DimensionsKey == ""
        && Span(fact) is > 0 and <= FiscalPeriodSpanDays.MaxAnnual;

    private static int Span(FinancialFact fact) =>
        fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;
}
