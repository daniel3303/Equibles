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

    [Fact]
    public void CompletedCutoff_AdmitsAQuarterExactlyWhenItsWindowCloses()
    {
        // For every day around the boundary: complete ⇔ window no longer open.
        for (var offset = 43; offset <= 48; offset++)
        {
            var today = QuarterEnd.AddDays(offset);
            var complete = QuarterEnd <= CombinedQuarterHelper.CompletedReportDateCutoff(today);

            Assert.Equal(!CombinedQuarterHelper.IsFilingWindowOpen(QuarterEnd, today), complete);
        }
    }
}
