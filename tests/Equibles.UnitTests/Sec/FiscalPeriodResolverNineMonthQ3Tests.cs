using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FiscalPeriodResolverNineMonthQ3Tests
{
    // Sibling to the Q1 quarterly-resolution pin. Resolve classifies the
    // (periodStart, periodEnd) duration into one of {Q1, Q2, Q3, Q4, FullYear}
    // via a `monthsElapsed` switch with the cumulative-arms
    //   <= 4 → Q1,  <= 7 → Q2,  <= 10 → Q3,  else → Q4.
    // The Q1 sibling pins the <=4 arm; this pin guards the <=10 arm with a
    // nine-month-duration filing (Jan-Sep of FY) that lands monthsElapsed = 8.
    // A copy-paste swap of the Q2 and Q3 case-bodies — or a refactor that
    // narrowed `<= 10` to `<= 9` — would silently mislabel every YTD-through-
    // Q3 SEC filing as Q2 (or Q4), corrupting the FinancialFact period column
    // for the most filed period of the year (companies report 10-Qs at Q1/Q2/Q3
    // and 10-K at year-end, so Q3 is the densest cohort).
    [Fact]
    public void Resolve_NineMonthYtdPeriodEndingSeptember_ReturnsQ3OfFiscalYear()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 1, 1),
            periodEnd: new DateOnly(2024, 9, 30),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().NotBeNull();
        result.Value.Year.Should().Be(2024);
        result.Value.Period.Should().Be(SecFiscalPeriod.Q3);
    }
}
