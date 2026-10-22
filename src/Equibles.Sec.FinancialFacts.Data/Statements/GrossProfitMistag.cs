using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// A reported gross profit that is the period's operating income is a mis-tag, not a subtotal:
/// some filers' 10-K quarterly tables tagged operating income as GrossProfit (EBS 2015–2019; Q3
/// 2016 is 35,478 in both). It is rejected only when Revenue − Cost of Revenue exists for the
/// period and states a different figure, so a filer whose gross profit truly equals its operating
/// income keeps its own.
/// </summary>
public static class GrossProfitMistag
{
    public const string GrossProfitAlias = "gross-profit";
    public const string OperatingIncomeAlias = "operating-income";
    public const string RevenueAlias = "revenue";
    public const string CostOfRevenueAlias = "cost-of-revenue";

    /// <summary>What a statement says about a gross-profit line it replaced.</summary>
    public const string ReplacedNote =
        "Gross Profit is Revenue − Cost of Revenue for a period whose figure tagged as gross "
        + "profit is its operating income.";

    /// <summary>True when <paramref name="concepts"/> is the gross-profit alias (or a synonym).</summary>
    public static bool IsGrossProfit(IReadOnlyList<FinancialConceptAliases.ConceptRef> concepts) =>
        FinancialConceptAliases.TryResolve(GrossProfitAlias, out var grossProfit)
        && ReferenceEquals(grossProfit, concepts);

    /// <summary>
    /// True when <paramref name="grossProfit"/> holds the period's operating income.
    /// <paramref name="operatingIncomeRounding"/> is the rounding a derived operating income
    /// inherits from its inputs: a fourth quarter synthesized as FY − (Q1+Q2+Q3) is only as
    /// precise as the year it subtracts (EBS restated FY2016 as 105.5M, so its Q4 misses the
    /// tagged 50,929,000 by 22,000).
    /// </summary>
    public static bool IsOperatingIncome(
        decimal grossProfit,
        decimal? operatingIncome,
        decimal? revenueLessCost,
        decimal operatingIncomeRounding = 1m
    ) =>
        grossProfit != 0
        && operatingIncome is { } income
        && income != 0
        && SameFigure(grossProfit, income, operatingIncomeRounding)
        && revenueLessCost is { } derived
        && !SameFigure(grossProfit, derived);

    /// <summary>
    /// Figures match within the coarser one's rounding, or within <paramref name="rounding"/>
    /// when that is coarser: a later 10-K restates the same quarter in tenths of millions (14.9M
    /// vs 14,910,000).
    /// </summary>
    public static bool SameFigure(decimal left, decimal right, decimal rounding = 1m) =>
        Math.Abs(left - right) * 2
        <= Math.Max(rounding, Math.Max(RoundingUnit(left), RoundingUnit(right)));

    /// <summary>The power of ten, up to a million, a stated figure is rounded to.</summary>
    public static decimal RoundingUnit(decimal value)
    {
        var unit = 1m;
        while (unit < 1_000_000m && value % (unit * 10) == 0)
            unit *= 10;
        return unit;
    }

    /// <summary>
    /// The rows of a gross-profit series that hold operating income, each paired with the other
    /// series' row for the same exact span and unit.
    /// </summary>
    public static List<FinancialFact> MistaggedRows(
        IEnumerable<FinancialFact> grossProfit,
        IEnumerable<FinancialFact> operatingIncome,
        IEnumerable<FinancialFact> revenue,
        IEnumerable<FinancialFact> costOfRevenue
    )
    {
        var income = ByPeriod(operatingIncome);
        var sales = ByPeriod(revenue);
        var cost = ByPeriod(costOfRevenue);
        return grossProfit
            .Where(f =>
                IsOperatingIncome(
                    f.Value,
                    income.TryGetValue(Key(f), out var operating) ? operating : null,
                    sales.TryGetValue(Key(f), out var top) && cost.TryGetValue(Key(f), out var c)
                        ? top - c
                        : null
                )
            )
            .ToList();
    }

    /// <summary>
    /// The warning a gross-profit series carries for its mis-tagged rows, or null when none is.
    /// </summary>
    public static string Warning(IReadOnlyCollection<FinancialFact> mistagged) =>
        WarningForPeriods(
            mistagged
                .OrderByDescending(f => f.PeriodEnd)
                .Select(f =>
                    f.FiscalPeriod == SecFiscalPeriod.FullYear
                        ? $"FY{f.FiscalYear}"
                        : $"FY{f.FiscalYear} {f.FiscalPeriod}"
                )
                .ToList()
        );

    /// <summary>
    /// The same warning over period labels ("FY2017 Q3"), newest first; null when there are none.
    /// </summary>
    public static string WarningForPeriods(IReadOnlyCollection<string> periodLabels) =>
        periodLabels.Count == 0
            ? null
            : $"Tag warning: for {string.Join(", ", periodLabels)} the figure tagged as gross "
                + "profit equals that period's operating income, while Revenue − Cost of Revenue "
                + "states a different figure.";

    private static (DateOnly Start, DateOnly End, string Unit) Key(FinancialFact fact) =>
        (fact.PeriodStart, fact.PeriodEnd, fact.Unit);

    private static Dictionary<(DateOnly Start, DateOnly End, string Unit), decimal> ByPeriod(
        IEnumerable<FinancialFact> facts
    )
    {
        var byPeriod = new Dictionary<(DateOnly, DateOnly, string), decimal>();
        foreach (var fact in facts)
            byPeriod.TryAdd(Key(fact), fact.Value);
        return byPeriod;
    }
}
