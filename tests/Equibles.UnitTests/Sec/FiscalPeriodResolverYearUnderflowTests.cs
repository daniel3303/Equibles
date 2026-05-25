using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FiscalPeriodResolverYearUnderflowTests
{
    [Fact(Skip = "GH-2069 — AddYears(-1) underflow on DateOnly.MinValue")]
    public void Resolve_PeriodEndYear1_DoesNotThrowOnCreateSafeUnderflow()
    {
        // Contract: CreateSafe clamps year < 1 to DateOnly.MinValue so that
        // candidate generation (periodEnd.Year - 1 = 0) never throws
        // ArgumentOutOfRangeException. Year-1 periods are synthetic but must
        // not crash the pipeline.
        var periodStart = new DateOnly(1, 1, 1);
        var periodEnd = new DateOnly(1, 6, 30);

        var result = FiscalPeriodResolver.Resolve(periodStart, periodEnd, 12, 31);

        result.Should().NotBeNull("year-1 + Dec FYE should resolve to FY1 Q2");
    }
}
