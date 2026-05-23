using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract: ParseDataSetEndDate returns null for unparseable file names.
/// TryParseDatePart parses day/month/year from the string and constructs a
/// DateOnly without validating the day-of-month — "31feb2026" passes string
/// parsing but throws ArgumentOutOfRangeException at DateOnly construction.
/// </summary>
public class Holdings13FRealtimeWorkerParseDataSetEndDateInvalidDayTests
{
    [Fact(Skip = "GH-1911 — ParseDataSetEndDate throws on invalid day-of-month")]
    public void ParseDataSetEndDate_InvalidDayOfMonth_ReturnsNullInsteadOfThrowing()
    {
        // Feb has at most 29 days; day 31 should be gracefully rejected, not throw.
        var act = () =>
            Holdings13FRealtimeWorker.ParseDataSetEndDate("01dec2025-31feb2026_form13f.zip");

        act.Should().NotThrow("unparseable dates must return null, not throw");
    }
}
