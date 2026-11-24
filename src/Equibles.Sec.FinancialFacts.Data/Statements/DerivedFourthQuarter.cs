using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// The fourth quarter of a series that shows a fiscal year and its first three quarters but no
/// fourth: the year less those three rows. It is worked out from the rows the series shows, so the
/// four quarters always add up to the year beside them.
/// </summary>
public static class DerivedFourthQuarter
{
    /// <summary>The picked rows plus each fourth quarter they imply.</summary>
    public static List<FinancialFact> Append(
        IReadOnlyCollection<FinancialFact> picked,
        bool rejectNegative
    )
    {
        var result = picked.ToList();
        foreach (var year in picked.Where(IsYear))
        {
            var rows = picked
                .Where(f =>
                    f != year
                    && f.PeriodType == FactPeriodType.Duration
                    && f.Unit == year.Unit
                    && f.PeriodStart >= year.PeriodStart
                    && f.PeriodEnd <= year.PeriodEnd
                    && Span(f) <= FiscalPeriodSpanDays.MaxQuarter
                )
                .OrderBy(f => f.PeriodStart)
                .ToList();
            // A quarter already ending with the year is a reported fourth quarter.
            if (rows.Count != 3 || rows[2].PeriodEnd >= year.PeriodEnd || !Chains(year, rows))
                continue;

            var start = rows[2].PeriodEnd.AddDays(1);
            if (
                year.PeriodEnd.DayNumber - start.DayNumber
                is < FiscalPeriodSpanDays.MinQuarter
                    or > FiscalPeriodSpanDays.MaxQuarter
            )
                continue;
            var value = year.Value - rows.Sum(f => f.Value);
            if (rejectNegative && value < 0m)
                continue;

            result.Add(
                new FinancialFact
                {
                    EquityIssuerId = year.EquityIssuerId,
                    FinancialConceptId = year.FinancialConceptId,
                    Unit = year.Unit,
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = start,
                    PeriodEnd = year.PeriodEnd,
                    Value = value,
                    FiscalYear = year.FiscalYear,
                    FiscalPeriod = SecFiscalPeriod.Q4,
                    FiledDate = rows.Append(year).Max(f => f.FiledDate),
                    DimensionsKey = year.DimensionsKey,
                }
            );
        }
        return result;
    }

    private static bool IsYear(FinancialFact fact) =>
        fact.FiscalPeriod == SecFiscalPeriod.FullYear
        && fact.PeriodType == FactPeriodType.Duration
        && Span(fact) is >= FiscalPeriodSpanDays.MinAnnual and <= FiscalPeriodSpanDays.MaxAnnual;

    // The year's first, second and third quarters, back to back from its first day.
    private static bool Chains(FinancialFact year, List<FinancialFact> quarters)
    {
        SecFiscalPeriod[] order = [SecFiscalPeriod.Q1, SecFiscalPeriod.Q2, SecFiscalPeriod.Q3];
        var expectedStart = year.PeriodStart;
        for (var index = 0; index < quarters.Count; index++)
        {
            var quarter = quarters[index];
            if (
                quarter.FiscalPeriod != order[index]
                || quarter.PeriodStart != expectedStart
                || Span(quarter) < FiscalPeriodSpanDays.MinQuarter
            )
                return false;
            expectedStart = quarter.PeriodEnd.AddDays(1);
        }
        return true;
    }

    private static int Span(FinancialFact fact) =>
        fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;
}
