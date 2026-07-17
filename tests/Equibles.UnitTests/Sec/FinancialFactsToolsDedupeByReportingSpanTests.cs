using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// A filing re-reports comparative prior periods under its OWN fiscal
/// identity, so a fromDate/toDate window that excludes a fiscal year's own
/// period end degenerates that (year, FY) group to the comparative alone —
/// and per-group picking then emits the SAME reporting span twice, once with
/// the wrong fiscal-year label. Live repro: NVDA eps-diluted, form=10-K,
/// toDate=2025-12-31 returned period end 2025-01-26 as both "FY 2025" and
/// "FY 2026" ($2.94 each). DedupeByReportingSpan keeps one row per actual
/// (PeriodStart, PeriodEnd, PeriodType) span, preferring the SMALLEST
/// fiscal-year stamp — the original filing's identity, since comparative
/// re-filings always re-stamp under the filing's later year.
/// </summary>
public class FinancialFactsToolsDedupeByReportingSpanTests
{
    private static FinancialFact AnnualFact(
        int fiscalYearStamp,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly filed
    ) =>
        new()
        {
            CommonStockId = Guid.NewGuid(),
            FinancialConceptId = Guid.NewGuid(),
            Value = 2.94m,
            Unit = "USD/shares",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            FiscalYear = fiscalYearStamp,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenK,
            FiledDate = filed,
            AccessionNumber = $"acc-{fiscalYearStamp}",
        };

    [Fact]
    public void DedupeByReportingSpan_ComparativeReStampedUnderLaterYear_KeepsOriginalFiscalYearOnly()
    {
        // The picked row from the (2025, FY) group — NVDA's own FY2025 filing.
        var original = AnnualFact(
            2025,
            new DateOnly(2024, 1, 29),
            new DateOnly(2025, 1, 26),
            new DateOnly(2025, 2, 26)
        );
        // The picked row from the degenerate (2026, FY) group — the FY2026
        // 10-K's comparative re-report of the SAME span, re-stamped 2026.
        var comparative = AnnualFact(
            2026,
            new DateOnly(2024, 1, 29),
            new DateOnly(2025, 1, 26),
            new DateOnly(2026, 2, 25)
        );
        // An untouched earlier year must pass through unchanged.
        var earlier = AnnualFact(
            2024,
            new DateOnly(2023, 1, 30),
            new DateOnly(2024, 1, 28),
            new DateOnly(2024, 2, 21)
        );

        var result = FinancialFactsTools.DedupeByReportingSpan([comparative, original, earlier]);

        result.Should().HaveCount(2, "the two picks covering one span collapse to one row");
        result
            .Single(f => f.PeriodEnd == new DateOnly(2025, 1, 26))
            .FiscalYear.Should()
            .Be(2025, "the smallest stamp is the period's own fiscal identity");
        result.Should().Contain(f => f.FiscalYear == 2024);
    }

    [Fact]
    public void DedupeByReportingSpan_DistinctSpansSharingAPeriodEnd_BothSurvive()
    {
        // A promoted discrete Q4 and the annual figure end the same day but
        // cover different spans — both are real rows and must both survive.
        var annual = AnnualFact(
            2020,
            new DateOnly(2019, 1, 28),
            new DateOnly(2020, 1, 26),
            new DateOnly(2020, 2, 20)
        );
        var promotedQ4 = AnnualFact(
            2020,
            new DateOnly(2019, 10, 28),
            new DateOnly(2020, 1, 26),
            new DateOnly(2020, 2, 20)
        );
        promotedQ4.FiscalPeriod = SecFiscalPeriod.Q4;

        var result = FinancialFactsTools.DedupeByReportingSpan([annual, promotedQ4]);

        result.Should().HaveCount(2);
    }
}
