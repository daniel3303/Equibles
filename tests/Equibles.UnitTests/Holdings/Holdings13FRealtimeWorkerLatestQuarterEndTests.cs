using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins LatestQuarterEnd: the end of the quarter immediately preceding the one
/// containing the given date, with the Jan–Mar roll back to the prior year's
/// 31 Dec. This is the basis of the realtime lookback floor (GH-2206).
/// </summary>
public class Holdings13FRealtimeWorkerLatestQuarterEndTests
{
    [Theory]
    [InlineData(2026, 1, 15, 2025, 12, 31)] // Jan → prior-year Dec 31
    [InlineData(2026, 3, 31, 2025, 12, 31)] // Mar 31 (Q1 not yet a filing season)
    [InlineData(2026, 4, 1, 2026, 3, 31)] // Apr → Mar 31
    [InlineData(2026, 5, 26, 2026, 3, 31)] // the Vanguard Q1 2026 case
    [InlineData(2026, 7, 10, 2026, 6, 30)] // Jul → Jun 30
    [InlineData(2026, 10, 1, 2026, 9, 30)] // Oct → Sep 30
    [InlineData(2026, 12, 31, 2026, 9, 30)] // Dec → Sep 30 (Q4 not yet ended)
    public void LatestQuarterEnd_ReturnsPrecedingQuarterEnd(
        int year,
        int month,
        int day,
        int expectedYear,
        int expectedMonth,
        int expectedDay
    )
    {
        var result = Holdings13FRealtimeWorker.LatestQuarterEnd(new DateOnly(year, month, day));

        result.Should().Be(new DateOnly(expectedYear, expectedMonth, expectedDay));
    }
}
