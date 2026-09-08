using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class StatementLineFactsBalanceSheetDateTests
{
    // GE's FY2019 bucket holds the 2019-12-31 year-end sheet and one subsequent-event instant
    // at 2020-01-01. The bucket's latest instant used to date the statement, so it rendered
    // that one line instead of the sheet. The flows end 2019-12-31, and the fullest dating
    // within a week of that is the sheet.
    [Fact]
    public void PickBalanceSheetDate_StrayLaterInstant_LosesToTheFullerSheet()
    {
        var picked = StatementLineFacts.PickBalanceSheetDate(
            new DateOnly(2019, 12, 31),
            [(new DateOnly(2019, 12, 31), 31), (new DateOnly(2020, 1, 1), 1)]
        );

        picked.Should().Be(new DateOnly(2019, 12, 31));
    }

    // The exact date is not privileged: ON's one-line 09-30 stray sits ON the flow end while
    // the 25-line sheet is dated two days earlier. The count decides, not the match.
    [Fact]
    public void PickBalanceSheetDate_OneLineStrayOnTheExactDate_LosesToTheSheetTwoDaysEarlier()
    {
        var picked = StatementLineFacts.PickBalanceSheetDate(
            new DateOnly(2024, 9, 30),
            [(new DateOnly(2024, 9, 30), 1), (new DateOnly(2024, 9, 28), 25)]
        );

        picked.Should().Be(new DateOnly(2024, 9, 28));
    }

    // HD stamps a May 2020 quarter's flows and its May 2021 balance sheet with the same
    // fiscal label. The 2021 sheet is a year from where the flows end, so it is not offered,
    // and the sheet that IS dated where the flows end is picked wherever it was stamped.
    [Fact]
    public void PickBalanceSheetDate_NextYearsSheetInTheSameBucket_IsNotWithinTolerance()
    {
        var picked = StatementLineFacts.PickBalanceSheetDate(
            new DateOnly(2020, 5, 3),
            [(new DateOnly(2021, 5, 2), 21), (new DateOnly(2020, 5, 3), 21)]
        );

        picked.Should().Be(new DateOnly(2020, 5, 3));
    }

    // A comparative column a year earlier and a stray a quarter later are both outside the
    // tolerance; with nothing inside it, the caller keeps its bucket-and-anchor path.
    [Fact]
    public void PickBalanceSheetDate_NothingWithinTolerance_IsNull()
    {
        var picked = StatementLineFacts.PickBalanceSheetDate(
            new DateOnly(2022, 12, 31),
            [(new DateOnly(2021, 12, 31), 30), (new DateOnly(2023, 3, 31), 30)]
        );

        picked.Should().BeNull();
    }

    // The tolerance is inclusive on both sides, and a date one day past it is out.
    [Theory]
    [InlineData(-7, true)]
    [InlineData(7, true)]
    [InlineData(-8, false)]
    [InlineData(8, false)]
    public void PickBalanceSheetDate_ToleranceIsSevenDaysInclusive(int offset, bool within)
    {
        var flowEnd = new DateOnly(2024, 6, 30);
        var stated = flowEnd.AddDays(offset);

        var picked = StatementLineFacts.PickBalanceSheetDate(flowEnd, [(stated, 20)]);

        (picked == stated).Should().Be(within);
        StatementLineFacts.BalanceSheetDateToleranceDays.Should().Be(7);
    }

    // DE dates one sheet 10-30 and 10-31 with the same lines. Equal counts fall to the date
    // nearest the flow end, and an equal distance to the earlier date, so the pick is
    // deterministic whatever order the dates arrive in.
    [Fact]
    public void PickBalanceSheetDate_EqualCounts_PreferNearestThenEarliest()
    {
        var flowEnd = new DateOnly(2022, 10, 30);

        StatementLineFacts
            .PickBalanceSheetDate(
                flowEnd,
                [(new DateOnly(2022, 10, 31), 28), (new DateOnly(2022, 10, 30), 28)]
            )
            .Should()
            .Be(new DateOnly(2022, 10, 30), "the sheet dated on the flow end is nearest");

        StatementLineFacts
            .PickBalanceSheetDate(
                flowEnd,
                [(new DateOnly(2022, 11, 1), 28), (new DateOnly(2022, 10, 28), 28)]
            )
            .Should()
            .Be(new DateOnly(2022, 10, 28), "two days either side, the earlier date stands");
    }

    // LAKE's FY2023 bucket holds its 23-concept year ending 2023-01-31 beside two one-concept
    // spans re-stamped from the next fiscal year, one of them ending latest. A plain maximum
    // would end the period in April 2024 and date the balance sheet fifteen months off.
    [Fact]
    public void PickFlowPeriodEnd_OneConceptSpanEndingLatest_LosesToTheFullestYear()
    {
        var picked = StatementLineFacts.PickFlowPeriodEnd([
            (new DateOnly(2024, 4, 30), 1),
            (new DateOnly(2023, 4, 30), 1),
            (new DateOnly(2023, 1, 31), 23),
        ]);

        picked.Should().Be(new DateOnly(2023, 1, 31));
    }

    // BALY (2025, Q2): the Jan 1..Feb 7 2025 predecessor stub carries 29 flow concepts (a whole
    // statement, cash flow included) beside the quarter's own 23, because a 10-Q's cash flow is
    // year-to-date and fails the span gate. The fullest span is the stub; the quarter still wins.
    [Fact]
    public void PickFlowPeriodEnd_PredecessorStubWithMoreConcepts_LosesToTheLaterQuarter()
    {
        var picked = StatementLineFacts.PickFlowPeriodEnd([
            (new DateOnly(2025, 2, 7), 29),
            (new DateOnly(2025, 3, 31), 1),
            (new DateOnly(2025, 6, 30), 23),
        ]);

        picked.Should().Be(new DateOnly(2025, 6, 30));
    }

    // The bar is half the fullest count, inclusive: a later span at exactly half still counts as a
    // measured period, one concept below it is a stray. Inclusive on purpose: NCO's (2026, Q2) own
    // quarter carries 3 flow concepts beside a 6-concept comparative in the same 10-Q, and a strict
    // majority would date that sheet a year early (26 buckets corpus-wide separate the two bars).
    [Theory]
    [InlineData(12, 2024)]
    [InlineData(11, 2023)]
    public void PickFlowPeriodEnd_HalfTheFullestCount_IsTheBar(int laterCount, int expectedYear)
    {
        var picked = StatementLineFacts.PickFlowPeriodEnd([
            (new DateOnly(2023, 1, 31), 24),
            (new DateOnly(2024, 4, 30), laterCount),
        ]);

        picked
            .Should()
            .Be(expectedYear == 2024 ? new DateOnly(2024, 4, 30) : new DateOnly(2023, 1, 31));
    }

    // Two periods measured with the same concepts: the later one is the current column and
    // the earlier one its comparative, so the tie goes to the latest end, as the old maximum did.
    [Fact]
    public void PickFlowPeriodEnd_EqualCounts_PreferTheLatestEnd()
    {
        var picked = StatementLineFacts.PickFlowPeriodEnd([
            (new DateOnly(2019, 5, 5), 11),
            (new DateOnly(2020, 5, 3), 11),
        ]);

        picked.Should().Be(new DateOnly(2020, 5, 3));
    }

    [Fact]
    public void PickFlowPeriodEnd_NothingMeasured_IsNull()
    {
        StatementLineFacts.PickFlowPeriodEnd([]).Should().BeNull();
    }

    [Fact]
    public void PickBalanceSheetDate_NoStatedDates_IsNull()
    {
        StatementLineFacts.PickBalanceSheetDate(new DateOnly(2024, 6, 30), []).Should().BeNull();
    }

    // The SQL predicate that finds where a period's flows end must agree with the in-memory
    // gate at every boundary, or a span the pick rejects dates the balance sheet.
    [Theory]
    [InlineData(349, false)]
    [InlineData(350, true)]
    [InlineData(380, true)]
    [InlineData(381, false)]
    public void MeasuresGranularityInSql_AgreesWithMeasuresGranularity_ForAFullYear(
        int spanDays,
        bool measures
    )
    {
        var fact = Span(spanDays);
        var inSql = StatementLineFacts.MeasuresGranularityInSql(SecFiscalPeriod.FullYear).Compile();

        inSql(fact).Should().Be(measures);
        StatementLineFacts
            .MeasuresGranularity(fact, SecFiscalPeriod.FullYear)
            .Should()
            .Be(measures);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void MeasuresGranularityInSql_AgreesWithMeasuresGranularity_ForAQuarter(
        int spanDays,
        bool measures
    )
    {
        var fact = Span(spanDays);
        var inSql = StatementLineFacts.MeasuresGranularityInSql(SecFiscalPeriod.Q2).Compile();

        inSql(fact).Should().Be(measures);
        StatementLineFacts.MeasuresGranularity(fact, SecFiscalPeriod.Q2).Should().Be(measures);
    }

    private static FinancialFact Span(int days) =>
        new()
        {
            CommonStockId = Guid.NewGuid(),
            FinancialConceptId = Guid.NewGuid(),
            Value = 1m,
            Unit = "USD",
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 1, 1).AddDays(days),
            FiscalYear = 2024,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            PeriodType = days == 0 ? FactPeriodType.Instant : FactPeriodType.Duration,
            Form = DocumentType.TenK,
            FiledDate = new DateOnly(2025, 2, 1),
            AccessionNumber = "0000000000-25-000001",
        };
}
