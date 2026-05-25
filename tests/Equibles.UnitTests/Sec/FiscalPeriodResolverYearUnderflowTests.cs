using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FiscalPeriodResolverYearUnderflowTests
{
    [Fact]
    public void Resolve_PeriodEndYear1_ReturnsNullInsteadOfThrowing()
    {
        // Contract: CreateSafe clamps year < 1 to DateOnly.MinValue so that
        // candidate generation (periodEnd.Year - 1 = 0) never throws
        // ArgumentOutOfRangeException. Year-1 periods are unresolvable.
        var periodStart = new DateOnly(1, 1, 1);
        var periodEnd = new DateOnly(1, 6, 30);

        var result = FiscalPeriodResolver.Resolve(periodStart, periodEnd, 12, 31);

        result.Should().BeNull("year-1 periods cannot produce a prior FYE");
    }
}
