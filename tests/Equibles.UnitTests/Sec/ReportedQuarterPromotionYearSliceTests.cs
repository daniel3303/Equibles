using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class ReportedQuarterPromotionYearSliceTests
{
    // CompareFinancialFact loads one fiscal-year stamp per company, and that slice
    // carries comparative re-filings: the year's own annual and Q4 PLUS the prior
    // year's annual and Q4 re-stamped under the requested year. Only the
    // quarter-span row ending exactly where the slice's own year does (the latest
    // annual-span period end) is the requested year's fourth quarter — promoting
    // the comparative would report the PRIOR year's quarter under this year's label.
    [Fact]
    public void PromotedFourthQuartersForYearSlice_SliceWithComparatives_PromotesOnlyTheOwnYearEnd()
    {
        var stockId = Guid.NewGuid();
        var slice = new List<FinancialFact>
        {
            // Own year (ends 2019-01-27).
            MakeFullYearFlow(
                stockId,
                11_716m,
                new DateOnly(2018, 1, 29),
                new DateOnly(2019, 1, 27)
            ),
            MakeFullYearFlow(
                stockId,
                2_205m,
                new DateOnly(2018, 10, 29),
                new DateOnly(2019, 1, 27)
            ),
            // Prior year re-reported as comparatives under the same stamp.
            MakeFullYearFlow(stockId, 9_714m, new DateOnly(2017, 1, 30), new DateOnly(2018, 1, 28)),
            MakeFullYearFlow(
                stockId,
                2_911m,
                new DateOnly(2017, 10, 30),
                new DateOnly(2018, 1, 28)
            ),
        };

        var promoted = ReportedQuarterPromotion.PromotedFourthQuartersForYearSlice(slice).ToList();

        var quarter = promoted.Should().ContainSingle("only the own-year Q4 qualifies").Subject;
        quarter.Value.Should().Be(2_205m);
        quarter.PeriodEnd.Should().Be(new DateOnly(2019, 1, 27));
        quarter.FiscalPeriod.Should().Be(SecFiscalPeriod.Q4);
    }

    // A slice whose own year reports no discrete fourth quarter (post-FY2020
    // filers) must promote nothing — the comparative quarter that IS present
    // belongs to the prior year and must not pose as this year's Q4.
    [Fact]
    public void PromotedFourthQuartersForYearSlice_OnlyComparativeQuarterPresent_PromotesNothing()
    {
        var stockId = Guid.NewGuid();
        var slice = new List<FinancialFact>
        {
            // Own year (ends 2021-01-31) — annual only, no discrete Q4.
            MakeFullYearFlow(
                stockId,
                16_675m,
                new DateOnly(2020, 1, 27),
                new DateOnly(2021, 1, 31)
            ),
            // Prior year's Q4 re-reported as a comparative under the same stamp.
            MakeFullYearFlow(
                stockId,
                3_105m,
                new DateOnly(2019, 10, 28),
                new DateOnly(2020, 1, 26)
            ),
        };

        var promoted = ReportedQuarterPromotion.PromotedFourthQuartersForYearSlice(slice).ToList();

        promoted.Should().BeEmpty("the comparative quarter belongs to the prior fiscal year");
    }

    // The annual total itself — the only other fp=FY row ending at the own year
    // end — spans a full year and must never be promoted to Q4.
    [Fact]
    public void PromotedFourthQuartersForYearSlice_AnnualSpanAtOwnYearEnd_IsNeverPromoted()
    {
        var stockId = Guid.NewGuid();
        var slice = new List<FinancialFact>
        {
            MakeFullYearFlow(
                stockId,
                10_918m,
                new DateOnly(2019, 1, 28),
                new DateOnly(2020, 1, 26)
            ),
        };

        var promoted = ReportedQuarterPromotion.PromotedFourthQuartersForYearSlice(slice).ToList();

        promoted.Should().BeEmpty("a full-year total may never masquerade as a quarter");
    }

    private static FinancialFact MakeFullYearFlow(
        Guid stockId,
        decimal value,
        DateOnly periodStart,
        DateOnly periodEnd
    ) =>
        new()
        {
            CommonStockId = stockId,
            FinancialConceptId = Guid.NewGuid(),
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = value,
            FiscalYear = 2019,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenK,
            FiledDate = periodEnd.AddDays(30),
            AccessionNumber = "0000000000-24-000001",
        };
}
