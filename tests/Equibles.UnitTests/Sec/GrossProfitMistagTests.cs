using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Operating income tagged as gross profit. Figures are EBS's: its 10-K quarterly tables tagged
/// operating income as GrossProfit for 2015–2019 (FY2016 Q3 is 35,478,000 in both).
/// </summary>
public class GrossProfitMistagTests
{
    [Theory]
    [InlineData(35_478_000L, 35_478_000L, 104_000_000L, true)]
    // The 2019 10-K restated FY2017 Q1 in tenths of millions; the 10-Q's exact figure stands.
    [InlineData(14_900_000L, 14_910_000L, 60_000_000L, true)]
    [InlineData(14_910_000L, 14_900_000L, 60_000_000L, true)]
    [InlineData(14_910_000L, 14_920_000L, 60_000_000L, false)]
    // No operating expenses: gross profit truly is operating income, and revenue − cost agrees.
    [InlineData(50_000_000L, 50_000_000L, 50_000_000L, false)]
    [InlineData(35_478_000L, 35_478_000L, null, false)]
    [InlineData(35_478_000L, null, 104_000_000L, false)]
    [InlineData(300_000L, 0L, 1_000_000L, false)]
    public void IsOperatingIncome_NeedsOperatingIncomeAndADifferingDerivedFigure(
        long grossProfit,
        long? operatingIncome,
        long? derived,
        bool expected
    )
    {
        GrossProfitMistag
            .IsOperatingIncome(grossProfit, operatingIncome, derived)
            .Should()
            .Be(expected);
    }

    // FY2016 Q4: the operating income is FY − (Q1+Q2+Q3) with the 2019 10-K's restated year of
    // 105,500,000, so it lands at 50,907,000 against the tagged 50,929,000; the year's own
    // filing said 105,522,000, which subtracts to exactly the tagged figure.
    [Fact]
    public void IsOperatingIncome_DerivedFromARoundedYear_MatchesWithinThatYearsRounding()
    {
        var operatingIncome = 105_500_000m - (21_157_000m - 2_042_000m + 35_478_000m);

        GrossProfitMistag
            .IsOperatingIncome(50_929_000m, operatingIncome, 118_389_000m)
            .Should()
            .BeFalse();
        GrossProfitMistag
            .IsOperatingIncome(
                50_929_000m,
                operatingIncome,
                118_389_000m,
                operatingIncomeRounding: GrossProfitMistag.RoundingUnit(105_500_000m)
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsOperatingIncome_RoundingFloor_StillNeedsADifferingDerivedFigure()
    {
        GrossProfitMistag
            .IsOperatingIncome(50_929_000m, 50_907_000m, 50_929_000m, 100_000m)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(105_522_000L, 1_000L)]
    [InlineData(105_500_000L, 100_000L)]
    [InlineData(14_900_000L, 100_000L)]
    [InlineData(2_000_000_000L, 1_000_000L)]
    [InlineData(-2_042_000L, 1_000L)]
    [InlineData(7L, 1L)]
    public void RoundingUnit_IsThePowerOfTenTheFigureIsStatedTo(long value, long unit)
    {
        GrossProfitMistag.RoundingUnit(value).Should().Be(unit);
    }

    [Fact]
    public void MistaggedRows_PairsEachRowWithTheSameSpanAndUnit()
    {
        var q3 = Quarter(2016, SecFiscalPeriod.Q3, new DateOnly(2016, 7, 1), 35_478_000m);
        var q2 = Quarter(2016, SecFiscalPeriod.Q2, new DateOnly(2016, 4, 1), -2_042_000m);

        var mistagged = GrossProfitMistag.MistaggedRows(
            [q3, q2],
            [
                Valued(q3, 35_478_000m),
                // Q2's operating income is reported only for another span: no pairing.
                Quarter(2016, SecFiscalPeriod.Q2, new DateOnly(2016, 4, 2), -2_042_000m),
            ],
            [Valued(q3, 142_914_000m), Valued(q2, 91_241_000m)],
            [Valued(q3, 39_560_000m), Valued(q2, 29_465_000m)]
        );

        mistagged.Should().Equal(q3);
    }

    [Fact]
    public void Warning_NamesThePeriodsNewestFirst()
    {
        GrossProfitMistag
            .Warning([
                Quarter(2016, SecFiscalPeriod.Q3, new DateOnly(2016, 7, 1), 1m),
                Quarter(2017, SecFiscalPeriod.Q1, new DateOnly(2017, 1, 1), 1m),
            ])
            .Should()
            .StartWith("Tag warning: for FY2017 Q1, FY2016 Q3 the figure tagged as gross profit");
        GrossProfitMistag.Warning([]).Should().BeNull();
    }

    private static FinancialFact Valued(FinancialFact fact, decimal value) =>
        Quarter(fact.FiscalYear, fact.FiscalPeriod, fact.PeriodStart, value);

    private static FinancialFact Quarter(
        int fiscalYear,
        SecFiscalPeriod period,
        DateOnly start,
        decimal value
    ) =>
        new()
        {
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = start,
            PeriodEnd = start.AddMonths(3).AddDays(-1),
            Value = value,
            FiscalYear = fiscalYear,
            FiscalPeriod = period,
        };
}
