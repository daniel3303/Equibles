using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Holdings13FReconciliationServiceReconcilableQuarterTests
{
    // The newest quarter a filer can be "late" on is the most recent quarter end
    // whose filing deadline has passed. Inside an open filing window the
    // service must reconcile through the PRIOR quarter, so it doesn't treat filers
    // that simply haven't filed yet as gaps.
    [Theory]
    // Well past Q1-2026's 15 May deadline → Q1 is reconcilable.
    [InlineData("2026-06-15", "2026-03-31")]
    // The day after Q1-2026's deadline (Fri 15 May) → Q1 is reconcilable.
    [InlineData("2026-05-16", "2026-03-31")]
    // On the deadline itself filers can still file on time → reconcile through Q4-2025.
    [InlineData("2026-05-15", "2025-12-31")]
    // Q3-2026's deadline falls on Sat 14 Nov and rolls to Mon 16 Nov → still Q2 that Monday.
    [InlineData("2026-11-16", "2026-06-30")]
    [InlineData("2026-11-17", "2026-09-30")]
    // New year, before Q4-2025's deadline (Sat 14 Feb, rolled past Washington's Birthday to Tue 17 Feb) → reconcile through Q3-2025.
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
