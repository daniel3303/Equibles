using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class ReportedQuarterPromotionHistoryTests
{
    // SEC Company Facts files a filer's reported discrete fourth quarter under
    // fp=FY (NVDA through FY2020): the ~90-day duration shares the annual figure's
    // fiscal identity, so stamp-based grouping never surfaces it as a Q4 row. The
    // history promotion must add a Q4-stamped copy — keyed to the year of the
    // annual-span fact ending the same day — while the annual total itself, being
    // a ~365-day span, must never qualify.
    [Fact]
    public void WithPromotedFourthQuarters_ReportedQ4UnderFullYearStamp_AddsQ4Copy()
    {
        var facts = new List<FinancialFact>
        {
            MakeFullYearFlow(2018, 2_911m, new DateOnly(2017, 10, 30), new DateOnly(2018, 1, 28)),
            MakeFullYearFlow(2018, 9_714m, new DateOnly(2017, 1, 30), new DateOnly(2018, 1, 28)),
        };

        var result = ReportedQuarterPromotion.WithPromotedFourthQuarters(facts);

        result.Should().HaveCount(3, "one Q4 copy joins the two original rows");
        var promoted = result.Single(f => f.FiscalPeriod == SecFiscalPeriod.Q4);
        promoted.Value.Should().Be(2_911m, "the promoted value is the quarter's, never the year's");
        promoted.FiscalYear.Should().Be(2018);
        promoted.Form.Should().Be(DocumentType.TenK, "the copy keeps the source filing's form");
        facts.Should().HaveCount(2, "the input list is never mutated");
    }

    // Comparative re-filings re-stamp the same discrete quarter under the filing's
    // later fiscal year. The promoted copy must take its year from the annual
    // anchor's smallest stamp — the original filing — so the row lands under the
    // year the quarter belongs to.
    [Fact]
    public void WithPromotedFourthQuarters_ComparativeReStampedVintage_UsesTheAnchorYear()
    {
        var facts = new List<FinancialFact>
        {
            // The FY2019 10-K re-reports FY2018's Q4 and annual under stamp 2019.
            MakeFullYearFlow(2019, 2_911m, new DateOnly(2017, 10, 30), new DateOnly(2018, 1, 28)),
            MakeFullYearFlow(2019, 9_714m, new DateOnly(2017, 1, 30), new DateOnly(2018, 1, 28)),
            // The original FY2018 10-K.
            MakeFullYearFlow(2018, 9_714m, new DateOnly(2017, 1, 30), new DateOnly(2018, 1, 28)),
        };

        var result = ReportedQuarterPromotion.WithPromotedFourthQuarters(facts);

        var promoted = result.Single(f => f.FiscalPeriod == SecFiscalPeriod.Q4);
        promoted
            .FiscalYear.Should()
            .Be(2018, "the smallest annual stamp is the original filing's own year");
    }

    // A fiscal-year-change transition "year" spans roughly a quarter with no
    // annual-span fact ending the same day. Without an anchor there is nothing to
    // promote — the stub keeps its annual identity rather than posing as Q4.
    [Fact]
    public void WithPromotedFourthQuarters_TransitionStubWithoutAnnualAnchor_PromotesNothing()
    {
        var facts = new List<FinancialFact>
        {
            MakeFullYearFlow(2024, 90m, new DateOnly(2024, 10, 1), new DateOnly(2024, 12, 31)),
        };

        var result = ReportedQuarterPromotion.WithPromotedFourthQuarters(facts);

        result.Should().BeSameAs(facts, "with nothing to promote the input is returned as-is");
    }

    // Annual-span rows alone can never produce a Q4: a full-year total must never
    // masquerade as a quarter.
    [Fact]
    public void WithPromotedFourthQuarters_AnnualSpansOnly_PromotesNothing()
    {
        var facts = new List<FinancialFact>
        {
            MakeFullYearFlow(2023, 380m, new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31)),
            MakeFullYearFlow(2024, 400m, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
        };

        var result = ReportedQuarterPromotion.WithPromotedFourthQuarters(facts);

        result
            .Should()
            .OnlyContain(
                f => f.FiscalPeriod == SecFiscalPeriod.FullYear,
                "no Q4 row may appear without a reported quarter-span duration"
            );
    }

    private static FinancialFact MakeFullYearFlow(
        int fiscalYear,
        decimal value,
        DateOnly periodStart,
        DateOnly periodEnd
    ) =>
        new()
        {
            CommonStockId = Guid.NewGuid(),
            FinancialConceptId = Guid.NewGuid(),
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = value,
            FiscalYear = fiscalYear,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenK,
            FiledDate = periodEnd.AddDays(30),
            AccessionNumber = "0000000000-24-000001",
        };
}
