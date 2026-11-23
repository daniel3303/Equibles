using Equibles.Fred.Data.Models;
using Equibles.Fred.Mcp.Helpers;

namespace Equibles.UnitTests.Fred;

public class YearOverYearTests
{
    private static Dictionary<DateOnly, FredObservation> Stated(
        params (string Date, decimal Value)[] rows
    ) =>
        rows.ToDictionary(
            row => DateOnly.Parse(row.Date),
            row => new FredObservation { Date = DateOnly.Parse(row.Date), Value = row.Value }
        );

    [Fact]
    public void Base_MonthlySeries_IsTheSameMonthAYearEarlier()
    {
        var stated = Stated(("2024-09-01", 314.851m), ("2024-10-01", 315.564m));

        YearOverYear.Base(new DateOnly(2025, 10, 1), "M", stated)!.Value.Should().Be(315.564m);
    }

    [Fact]
    public void Base_WeeklySeries_IsFiftyTwoWeeksEarlier()
    {
        // MORTGAGE30US closes on Thursdays: 2025-11-20 against 2024-11-21, not the calendar date.
        var stated = Stated(("2024-11-21", 6.84m), ("2024-11-14", 6.78m));

        YearOverYear.Base(new DateOnly(2025, 11, 20), "W", stated)!.Value.Should().Be(6.84m);
    }

    [Fact]
    public void Base_DailySeriesOnAWeekend_TakesTheLastCloseBefore()
    {
        // 2025-11-17 a year earlier is Sunday 2024-11-17; the base is Friday's close.
        var stated = Stated(("2024-11-14", 4.45m), ("2024-11-15", 4.43m));

        YearOverYear.Base(new DateOnly(2025, 11, 17), "D", stated)!.Value.Should().Be(4.43m);
    }

    [Fact]
    public void Base_FrequencyStoredAsAName_ReadsLikeItsCode()
    {
        var stated = Stated(("2024-11-21", 6.84m));

        YearOverYear
            .Base(new DateOnly(2025, 11, 20), "Weekly, Ending Thursday", stated)!
            .Value.Should()
            .Be(6.84m);
    }

    [Fact]
    public void Base_NothingStoredAYearEarlier_IsNull() =>
        YearOverYear.Base(new DateOnly(2020, 3, 1), "M", Stated()).Should().BeNull();

    [Fact]
    public void Change_Level_IsAPercentChange() =>
        // CPI October 2025 against October 2024.
        YearOverYear.Change(324.368m, 315.564m, inPoints: false).Should().Be(2.79m);

    [Fact]
    public void Change_SeriesInPercent_IsADifferenceInPoints() =>
        YearOverYear.Change(4.4m, 4.1m, inPoints: true).Should().Be(0.3m);

    [Theory]
    [InlineData(0)]
    [InlineData(-12.5)]
    public void Change_LevelFromABaseAtOrBelowZero_HasNoPercentChange(double yearEarlier) =>
        YearOverYear.Change(5m, (decimal)yearEarlier, inPoints: false).Should().BeNull();

    [Theory]
    [InlineData("Percent", true)]
    [InlineData("Percent of GDP", true)]
    [InlineData("Index 1982-1984=100", false)]
    [InlineData("Billions of Dollars", false)]
    public void InPoints_ReadsTheUnits(string units, bool inPoints) =>
        YearOverYear.InPoints(units).Should().Be(inPoints);

    [Fact]
    public void AlreadyAChange_SeriesStatedAsAChange_IsRecognised() =>
        YearOverYear.AlreadyAChange("Percent Change from Preceding Period").Should().BeTrue();
}
