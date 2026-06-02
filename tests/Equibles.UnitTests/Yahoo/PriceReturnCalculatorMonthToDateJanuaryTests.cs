using Equibles.Yahoo.Repositories;

namespace Equibles.UnitTests.Yahoo;

public class PriceReturnCalculatorMonthToDateJanuaryTests
{
    // Contract: MonthToDate anchors on the last close strictly before the first day of the
    // latest bar's month. When the latest bar falls in January that month-start threshold is
    // Jan 1, so the anchor must reach across the year boundary to the prior December's final
    // close — the very same bar YearToDate anchors on. The two windows therefore have to
    // coincide in January. Existing MTD coverage only spans an in-year (April→May) anchor, so
    // this pins the year-boundary arm the month-start threshold computation relies on.
    [Fact]
    public void Compute_LatestBarInJanuary_MonthToDateAnchorsOnPriorDecemberCloseLikeYearToDate()
    {
        var dates = new List<DateOnly>
        {
            new(2024, 12, 30),
            new(2024, 12, 31),
            new(2025, 1, 2),
            new(2025, 1, 3),
            new(2025, 1, 6),
        };
        // Dec 31, 2024 close (100) is both the prior-month-end and prior-year-end anchor;
        // latest close 125 → (125 / 100 - 1) * 100 = 25%.
        var closes = new List<decimal> { 90m, 100m, 110m, 120m, 125m };

        var result = PriceReturnCalculator.Compute(dates, closes);

        result.MonthToDate.Should().Be(25m);
        result.MonthToDate.Should().Be(result.YearToDate);
    }
}
