using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FiscalPeriodResolverHalfYearQ2Tests
{
    // Sibling to the Q1 (quarterly) and Q3 (nine-month) duration pins —
    // closes the Q2 (half-year) arm of `Resolve`'s monthsElapsed switch.
    // 20-F / 6-K filers (foreign issuers) and Russell-2000 names report
    // semi-annual cohorts: Jan–Jun spans ~181 days inside the
    // HalfYearMinDays..HalfYearMaxDays band, lands monthsElapsed = 5,
    // and must classify as Q2 via the `<= 7` arm. A copy-paste swap of
    // the Q1 and Q2 case bodies would mislabel every half-year filing
    // as Q1 and corrupt the FinancialFact period column for the entire
    // semi-annual cohort.
    [Fact]
    public void Resolve_HalfYearPeriodEndingJune_ReturnsQ2OfFiscalYear()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 1, 1),
            periodEnd: new DateOnly(2024, 6, 30),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().NotBeNull();
        result.Value.Year.Should().Be(2024);
        result.Value.Period.Should().Be(SecFiscalPeriod.Q2);
    }
}
