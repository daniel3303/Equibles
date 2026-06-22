using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementToolsPickCurrentlyReportedFactQuarterSpanTests
{
    // The income-statement half of #1546/#3835: a 10-Q tags each flow line twice under the same
    // fiscal (year, period) — the discrete quarter AND the fiscal year-to-date span — and both
    // end on the same period-end date, so the PeriodEnd/FiledDate tiebreaks can't separate them.
    // The contract resolves this by the span filter: prefer the span matching the period's
    // granularity, so a quarter request must return the discrete-quarter figure, never the larger
    // YTD cumulative. The YTD is listed first with the same filed date to pin that the span filter
    // (not input order or filed date) does the selection. The instant-only sibling test never
    // exercises this duration branch.
    [Fact]
    public void PickCurrentlyReportedFact_QuarterWithYearToDateSpanSamePeriodEnd_PicksDiscreteQuarter()
    {
        var conceptId = Guid.NewGuid();
        var periodEnd = new DateOnly(2025, 3, 29);

        // ~26-week cumulative span ending on the same date — must be rejected for a quarter request.
        var yearToDate = MakeDuration(
            conceptId,
            value: 210_000m,
            periodStart: new DateOnly(2024, 9, 29),
            periodEnd: periodEnd
        );
        // ~13-week discrete quarter ending on the same date — the currently-reported quarter figure.
        var discreteQuarter = MakeDuration(
            conceptId,
            value: 95_000m,
            periodStart: new DateOnly(2025, 1, 1),
            periodEnd: periodEnd
        );

        var result = FinancialStatementTools.PickCurrentlyReportedFact(
            [yearToDate, discreteQuarter],
            SecFiscalPeriod.Q2
        );

        result
            .Value.Should()
            .Be(95_000m, "a quarter request resolves to the discrete-quarter span, not the YTD");
    }

    private static FinancialFact MakeDuration(
        Guid conceptId,
        decimal value,
        DateOnly periodStart,
        DateOnly periodEnd
    ) =>
        new()
        {
            CommonStockId = Guid.NewGuid(),
            FinancialConceptId = conceptId,
            Value = value,
            Unit = "USD",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            FiscalYear = 2025,
            FiscalPeriod = SecFiscalPeriod.Q2,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TenQ,
            FiledDate = new DateOnly(2025, 5, 2),
            AccessionNumber = "0000320193-25-000057",
        };
}
