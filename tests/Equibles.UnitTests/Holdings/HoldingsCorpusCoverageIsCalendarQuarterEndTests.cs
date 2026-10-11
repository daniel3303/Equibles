using Equibles.Holdings.BusinessLogic;

namespace Equibles.UnitTests.Holdings;

// Form 13F reports as of a calendar quarter end; both import paths refuse any other period.
public class HoldingsCorpusCoverageIsCalendarQuarterEndTests
{
    [Theory]
    [InlineData(2020, 3, 31)]
    [InlineData(2020, 6, 30)]
    [InlineData(2020, 9, 30)]
    [InlineData(2020, 12, 31)]
    public void IsCalendarQuarterEnd_LastDayOfAQuarterMonth_IsTrue(int year, int month, int day)
    {
        HoldingsCorpusCoverage
            .IsCalendarQuarterEnd(new DateOnly(year, month, day))
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData(2020, 8, 14)] // a signature date typed as the period
    [InlineData(2020, 6, 29)] // one day short of the quarter end
    [InlineData(2024, 2, 29)] // a month end outside a quarter month
    [InlineData(2020, 9, 1)]
    public void IsCalendarQuarterEnd_AnyOtherDay_IsFalse(int year, int month, int day)
    {
        HoldingsCorpusCoverage
            .IsCalendarQuarterEnd(new DateOnly(year, month, day))
            .Should()
            .BeFalse();
    }
}
