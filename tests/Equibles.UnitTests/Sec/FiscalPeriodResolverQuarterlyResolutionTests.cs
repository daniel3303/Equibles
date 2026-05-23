using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

// Lane B (coverage): exercises the quarterly resolution path in Resolve
// (lines 67-108) — zero-hit today. Existing tests cover annual/instant
// periods and the FYE-validation guard; the quarter/half/nine-month
// duration classification and endingFye matching are uncovered.
public class FiscalPeriodResolverQuarterlyResolutionTests
{
    [Fact]
    public void Resolve_CalendarQ1QuarterPeriod_ReturnsQ1OfCorrectFiscalYear()
    {
        // FYE Dec 31, period Jan 1 - Mar 31 = 90 days (within 80-100 quarter range).
        // Ending FYE = Dec 31, 2024. Prior FYE = Dec 31, 2023. FiscalYearStart = Jan 1, 2024.
        // MonthsElapsed = Mar - Jan = 2 months. Switch: <= 4 => Q1.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 1, 1),
            periodEnd: new DateOnly(2024, 3, 31),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().NotBeNull();
        result.Value.Year.Should().Be(2024);
        result.Value.Period.Should().Be(SecFiscalPeriod.Q1);
    }
}
