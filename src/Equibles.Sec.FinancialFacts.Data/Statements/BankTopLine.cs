using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// A bank's top line is net interest income plus noninterest income. Its <c>Revenues</c> tag can be
/// one slice of that (Zions FY2026 Q2: Revenues 182M against 677M net interest income plus 460M
/// noninterest income) or the same top line on another basis (US Bancorp FY2025: a
/// taxable-equivalent 28,656M against 16,649M plus 11,891M), and many banks tag no revenue concept
/// at all, so the sum stands in for the revenue alias wherever revenue is missing, partial or
/// restated. Revenue before interest expense is a broader line the filer states and is kept: it
/// sits above the sum by the period's interest expense (Raymond James, quarter ended 2020-12-31:
/// 2,260M against 165M plus 2,057M, with 38M of interest expense).
/// </summary>
public static class BankTopLine
{
    public const string RevenueAlias = "revenue";
    public const string NetInterestIncomeAlias = "net-interest-income";
    public const string NoninterestIncomeAlias = "noninterest-income";
    public const string InterestExpenseAlias = "interest-expense";

    /// <summary>
    /// The concept id every summed fact carries. No stored concept has it, so a sum never collides
    /// with a component's own facts and its derived quarters subtract like any one concept.
    /// </summary>
    public static readonly Guid ConceptId = new("b4a1e7c2-3f5d-4e86-9a0b-6c2d8e1f7a53");

    // The same top line on another basis sits a fraction of a percent above the bank sum (a
    // taxable-equivalent adjustment); anything further above is a broader line. Revenue before
    // interest expense falls inside this margin when rates are low, so the margin alone cannot
    // tell the two apart: the period's interest expense or the filer's other periods do.
    private const decimal OtherBasisMargin = 0.02m;

    // Rounding between the revenue line and the three figures it is compared with.
    private const decimal InterestExpenseTolerance = 0.001m;

    /// <summary>
    /// True when <paramref name="concepts"/> is the revenue alias (or one of its synonyms, which
    /// resolve to the same list).
    /// </summary>
    public static bool IsRevenue(IReadOnlyList<FinancialConceptAliases.ConceptRef> concepts) =>
        FinancialConceptAliases.TryResolve(RevenueAlias, out var revenue)
        && ReferenceEquals(revenue, concepts);

    /// <summary>
    /// The revenue alias's facts with the bank sum folded in, and the pick priority that goes with
    /// them. A sum pairs the two components one filing states for one exact period. When the newest
    /// revenue period differs from the sum without being a broader line, revenue is a slice of the
    /// top line or states it on another basis, and the sums replace it in every fiscal year they
    /// reach; otherwise they only fill fiscal years with no revenue. A line above the sum is another
    /// basis only while none of its periods sits further above than that margin, so one filer keeps
    /// one basis through low and high rates; a caller holding a slice of the filer's history passes
    /// <paramref name="interestExpense"/>, which proves revenue before interest expense in the
    /// newest period alone. Each fiscal year keeps one
    /// measure, so a derived fourth quarter (the year less its first three quarters) never subtracts
    /// one measure from the other.
    /// </summary>
    public static (List<FinancialFact> Facts, Dictionary<Guid, int> Priority) Fold(
        IReadOnlyCollection<FinancialFact> revenue,
        IReadOnlyCollection<FinancialFact> netInterestIncome,
        IReadOnlyCollection<FinancialFact> noninterestIncome,
        IReadOnlyDictionary<Guid, int> revenuePriority,
        IReadOnlyCollection<FinancialFact> interestExpense = null
    )
    {
        var priority = new Dictionary<Guid, int>(revenuePriority);
        var sums = Sums(netInterestIncome, noninterestIncome);
        if (sums.Count == 0)
            return ([.. revenue], priority);

        priority[ConceptId] = int.MaxValue;
        if (SumIsTopLineAtNewest(revenue, sums, revenuePriority, interestExpense ?? []))
        {
            var sumYears = sums.Select(f => f.FiscalYear).ToHashSet();
            return ([.. revenue.Where(f => !sumYears.Contains(f.FiscalYear)), .. sums], priority);
        }
        var revenueYears = revenue
            .Where(IsConsolidatedDuration)
            .Select(f => f.FiscalYear)
            .ToHashSet();
        return ([.. revenue, .. sums.Where(f => !revenueYears.Contains(f.FiscalYear))], priority);
    }

    /// <summary>
    /// One period's top line, for a statement that shows a single period: the bank sum where the
    /// period has no revenue or its revenue is a slice or another basis of the sum, else the revenue.
    /// The components may come from different filings, as each statement line takes its own latest
    /// restatement; a derived component makes the sum derived. A single period shows none of the
    /// filer's history, so <paramref name="interestExpense"/> is what proves revenue before
    /// interest expense here.
    /// </summary>
    public static FinancialFact PickForPeriod(
        FinancialFact revenue,
        FinancialFact netInterestIncome,
        FinancialFact noninterestIncome,
        FinancialFact interestExpense = null
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
        if (!SamePeriod(revenue, sum))
            return revenue;
        var samePeriodInterestExpense =
            interestExpense != null && SamePeriod(interestExpense, sum) ? interestExpense : null;
        return SumIsTopLine(revenue, sum, samePeriodInterestExpense) ? sum : revenue;
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
    private static bool SumIsTopLineAtNewest(
        IReadOnlyCollection<FinancialFact> revenue,
        IReadOnlyCollection<FinancialFact> sums,
        IReadOnlyDictionary<Guid, int> revenuePriority,
        IReadOnlyCollection<FinancialFact> interestExpense
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

        var sum = SumFor(newest, sums);
        var newestInterestExpense = interestExpense
            .Where(f => IsConsolidatedDuration(f) && SamePeriod(f, newest))
            .OrderByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.AccessionNumber, StringComparer.Ordinal)
            .FirstOrDefault();
        if (sum == null || !SumIsTopLine(newest, sum, newestInterestExpense))
            return false;
        return newest.Value < sum.Value
            || !revenue
                .Where(f =>
                    f.FinancialConceptId == newest.FinancialConceptId && IsConsolidatedDuration(f)
                )
                .Any(f => SumFor(f, sums) is { } own && IsBroaderLine(f, own));
    }

    private static FinancialFact SumFor(
        FinancialFact revenue,
        IReadOnlyCollection<FinancialFact> sums
    ) =>
        sums.Where(s => SamePeriod(s, revenue))
            .OrderByDescending(s => s.FiledDate)
            .ThenByDescending(s => s.AccessionNumber, StringComparer.Ordinal)
            .FirstOrDefault();

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

    // An equal line is the sum itself and keeps its own tag and filing; a line above the sum by
    // the period's interest expense is revenue before interest expense, however small that is.
    private static bool SumIsTopLine(
        FinancialFact direct,
        FinancialFact topLine,
        FinancialFact interestExpense = null
    )
    {
        if (direct.Value == topLine.Value)
            return false;
        var above = direct.Value - topLine.Value;
        if (above < 0m)
            return true;
        if (
            interestExpense != null
            && Math.Abs(above - interestExpense.Value)
                <= Math.Abs(topLine.Value) * InterestExpenseTolerance
        )
            return false;
        return !IsBroaderLine(direct, topLine);
    }

    private static bool IsBroaderLine(FinancialFact direct, FinancialFact topLine) =>
        direct.Value - topLine.Value > Math.Abs(topLine.Value) * OtherBasisMargin;

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
