using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// The 45-day 13F filing-window rule every surface resolves the current quarter through. The
/// two sides must stay exactly complementary: a quarter is presented as the combined view while
/// IsFilingWindowOpen and may anchor QoQ verdicts only once its date falls at or before
/// CompletedReportDateCutoff. If the boundaries ever drift apart, a quarter could be both
/// "still filling in" and "complete" (or neither) on the same day.
/// </summary>
public class CombinedQuarterHelperWindowBoundaryTests
{
    private static readonly DateOnly QuarterEnd = new(2026, 3, 31);

    [Fact]
    public void IsFilingWindowOpen_OnTheDeadline_IsStillOpen()
    {
        // Deadline day (quarter end + 45): filings are still legally arriving.
        var deadline = QuarterEnd.AddDays(CombinedQuarterHelper.FilingDeadlineDays);

        Assert.True(CombinedQuarterHelper.IsFilingWindowOpen(QuarterEnd, deadline));
        Assert.False(CombinedQuarterHelper.IsFilingWindowOpen(QuarterEnd, deadline.AddDays(1)));
    }

    [Theory]
    // Friday deadline: no roll.
    [InlineData("2026-03-31", "2026-05-15")]
    // Saturday Nov 14 rolls to Monday Nov 16.
    [InlineData("2026-09-30", "2026-11-16")]
    // Sunday Feb 14 rolls past Washington's Birthday (Mon Feb 15) to Tuesday Feb 16.
    [InlineData("2026-12-31", "2027-02-16")]
    public void FilingDeadline_RollsAWeekendOrHolidayDeadlineToTheNextBusinessDay(
        string quarterEnd,
        string expected
    )
    {
        Assert.Equal(
            DateOnly.Parse(expected),
            CombinedQuarterHelper.FilingDeadline(DateOnly.Parse(quarterEnd))
        );
    }

    [Fact]
    public void IsFilingWindowOpen_StaysOpenThroughARolledDeadline()
    {
        var quarterEnd = new DateOnly(2026, 9, 30);

        Assert.True(
            CombinedQuarterHelper.IsFilingWindowOpen(quarterEnd, new DateOnly(2026, 11, 15))
        );
        Assert.True(
            CombinedQuarterHelper.IsFilingWindowOpen(quarterEnd, new DateOnly(2026, 11, 16))
        );
        Assert.False(
            CombinedQuarterHelper.IsFilingWindowOpen(quarterEnd, new DateOnly(2026, 11, 17))
        );
    }

    [Theory]
    [InlineData("2026-03-31")]
    [InlineData("2026-06-30")]
    [InlineData("2026-09-30")]
    [InlineData("2026-12-31")]
    public void CompletedCutoff_AdmitsAQuarterExactlyWhenItsWindowCloses(string quarterEnd)
    {
        // For every day around the boundary, rolled or not: complete ⇔ window no longer open.
        var reportDate = DateOnly.Parse(quarterEnd);
        for (var offset = 43; offset <= 50; offset++)
        {
            var today = reportDate.AddDays(offset);
            var complete = reportDate <= CombinedQuarterHelper.CompletedReportDateCutoff(today);

            Assert.Equal(!CombinedQuarterHelper.IsFilingWindowOpen(reportDate, today), complete);
        }
    }
}
