using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Holdings13FReconciliationServiceReconcilableQuarterTests
{
    // The newest quarter a filer can be "late" on is the most recent quarter end
    // whose 45-day filing deadline has elapsed. Inside an open filing window the
    // service must reconcile through the PRIOR quarter, so it doesn't treat filers
    // that simply haven't filed yet as gaps.
    [Theory]
    // Well past Q1-2026's 15 May deadline → Q1 is reconcilable.
    [InlineData("2026-06-15", "2026-03-31")]
    // Exactly on Q1-2026's deadline (31 Mar + 45 days) → counts as elapsed.
    [InlineData("2026-05-15", "2026-03-31")]
    // One day before that deadline → still reconcile only through Q4-2025.
    [InlineData("2026-05-14", "2025-12-31")]
    // New year, before Q4-2025's 14 Feb deadline → reconcile through Q3-2025.
    [InlineData("2026-01-01", "2025-09-30")]
    // Just after Q3-2025's 14 Nov deadline → Q3 reconcilable.
    [InlineData("2025-11-20", "2025-09-30")]
    public void LatestReconcilableQuarterEnd_GatesOnTheFilingDeadline(string today, string expected)
    {
        var result = Holdings13FReconciliationService.LatestReconcilableQuarterEnd(
            DateOnly.Parse(today)
        );

        result.Should().Be(DateOnly.Parse(expected));
    }
}
