using Equibles.Yahoo.Repositories;

namespace Equibles.UnitTests.Yahoo;

public class PriceReturnCalculatorTests
{
    // Build a close series ending on `lastDate`, one bar per consecutive day going
    // backwards. closes[^1] is the latest. Dates step back by one calendar day per
    // bar — fine for trailing-window tests (which only count bars, not the gap).
    private static (List<DateOnly> Dates, List<decimal> Closes) Series(
        DateOnly lastDate,
        params decimal[] closesOldestFirst
    )
    {
        var dates = new List<DateOnly>();
        var start = lastDate.AddDays(-(closesOldestFirst.Length - 1));
        for (var i = 0; i < closesOldestFirst.Length; i++)
            dates.Add(start.AddDays(i));
        return (dates, closesOldestFirst.ToList());
    }

    [Fact]
    public void Compute_MismatchedLengths_Throws()
    {
        var act = () =>
            PriceReturnCalculator.Compute(
                [new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2)],
                [100m]
            );

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Compute_EmptySeries_AllReturnsNull()
    {
        var result = PriceReturnCalculator.Compute([], []);

        result.FiveDay.Should().BeNull();
        result.TwentyDay.Should().BeNull();
        result.OneHundredTwentyDay.Should().BeNull();
        result.MonthToDate.Should().BeNull();
        result.YearToDate.Should().BeNull();
    }

    [Fact]
    public void Compute_FiveDay_ComparesLatestToBarFiveBack()
    {
        // 7 bars; the close 5 bars before the last (index 1 = 100) is the base,
        // latest = 110 → (110/100 - 1) * 100 = 10.
        var (dates, closes) = Series(
            new DateOnly(2025, 6, 10),
            90m,
            100m,
            102m,
            104m,
            106m,
            108m,
            110m
        );

        var result = PriceReturnCalculator.Compute(dates, closes);

        result.FiveDay.Should().Be(10m);
    }

    [Fact]
    public void Compute_FiveDay_TooFewBars_Null()
    {
        // 5 bars — a 5-day return needs 6 (base is 5 bars back).
        var (dates, closes) = Series(new DateOnly(2025, 6, 10), 100m, 101m, 102m, 103m, 104m);

        var result = PriceReturnCalculator.Compute(dates, closes);

        result.FiveDay.Should().BeNull();
    }

    [Fact]
    public void Compute_TwentyAndOneHundredTwentyDay_RespectWindowLength()
    {
        // 121 bars: enough for the 120-day window (needs 121) and the 20-day window,
        // but exactly at the 120 boundary.
        var closes = new decimal[121];
        for (var i = 0; i < closes.Length; i++)
            closes[i] = 100m + i; // strictly rising
        var (dates, list) = Series(new DateOnly(2025, 12, 31), closes);

        var result = PriceReturnCalculator.Compute(dates, list);

        // 20-day base = closes[^21] = closes[100] = 200; latest = closes[120] = 220.
        result.TwentyDay.Should().Be(Math.Round((220m / 200m - 1m) * 100m, 2));
        // 120-day base = closes[0] = 100; latest = 220 → +120%.
        result.OneHundredTwentyDay.Should().Be(120m);
    }

    [Fact]
    public void Compute_OneHundredTwentyDay_TooFewBars_Null()
    {
        var closes = new decimal[120];
        for (var i = 0; i < closes.Length; i++)
            closes[i] = 100m + i;
        var (dates, list) = Series(new DateOnly(2025, 12, 31), closes);

        var result = PriceReturnCalculator.Compute(dates, list);

        result.OneHundredTwentyDay.Should().BeNull();
    }

    [Fact]
    public void Compute_MonthToDate_AnchorsOnPriorMonthFinalClose()
    {
        // Bars span late April into May. The last April bar (Apr 30, close 200) is the
        // MTD base; latest May bar close = 220 → +10%.
        var dates = new List<DateOnly>
        {
            new(2025, 4, 28),
            new(2025, 4, 29),
            new(2025, 4, 30),
            new(2025, 5, 1),
            new(2025, 5, 2),
        };
        var closes = new List<decimal> { 180m, 190m, 200m, 210m, 220m };

        var result = PriceReturnCalculator.Compute(dates, closes);

        result.MonthToDate.Should().Be(10m);
    }

    [Fact]
    public void Compute_MonthToDate_NoPriorMonthBar_Null()
    {
        // All bars are in the same (latest) month — no prior-month close to anchor on.
        var dates = new List<DateOnly> { new(2025, 5, 1), new(2025, 5, 2), new(2025, 5, 3) };
        var closes = new List<decimal> { 100m, 110m, 120m };

        var result = PriceReturnCalculator.Compute(dates, closes);

        result.MonthToDate.Should().BeNull();
    }

    [Fact]
    public void Compute_YearToDate_AnchorsOnPriorYearFinalClose()
    {
        // Dec 31 prior year (close 100) is the YTD base; latest = 150 → +50%.
        var dates = new List<DateOnly>
        {
            new(2024, 12, 30),
            new(2024, 12, 31),
            new(2025, 1, 2),
            new(2025, 1, 3),
        };
        var closes = new List<decimal> { 95m, 100m, 130m, 150m };

        var result = PriceReturnCalculator.Compute(dates, closes);

        result.YearToDate.Should().Be(50m);
    }

    [Fact]
    public void Compute_YearToDate_NoPriorYearBar_Null()
    {
        var dates = new List<DateOnly> { new(2025, 1, 2), new(2025, 1, 3) };
        var closes = new List<decimal> { 100m, 110m };

        var result = PriceReturnCalculator.Compute(dates, closes);

        result.YearToDate.Should().BeNull();
    }

    [Fact]
    public void Compute_NonPositiveBaseClose_Null()
    {
        // 6 bars so the 5-day window is in range; the base bar (5 back) is zero,
        // which can't yield a percentage → null rather than divide-by-zero.
        var (dates, closes) = Series(new DateOnly(2025, 6, 10), 0m, 10m, 20m, 30m, 40m, 50m);

        var result = PriceReturnCalculator.Compute(dates, closes);

        result.FiveDay.Should().BeNull();
    }

    [Fact]
    public void Compute_NegativeReturn_IsNegative()
    {
        var (dates, closes) = Series(new DateOnly(2025, 6, 10), 100m, 99m, 98m, 97m, 96m, 80m);

        var result = PriceReturnCalculator.Compute(dates, closes);

        result.FiveDay.Should().Be(-20m);
    }
}
