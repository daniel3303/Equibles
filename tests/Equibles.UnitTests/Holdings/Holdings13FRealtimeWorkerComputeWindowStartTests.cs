using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins ComputeWindowStart, the incremental-sweep window (GH-2241). With a
/// watermark the sweep resumes from it but never covers fewer than the trailing
/// re-sweep window (so late/amended filings are still caught); with no watermark
/// (cold start) it falls back to the quarter-floor lookback.
/// </summary>
public class Holdings13FRealtimeWorkerComputeWindowStartTests
{
    // today = 2026-05-27, trailing = 14 → trailing window starts 2026-05-13.
    [Theory]
    [InlineData(2026, 5, 26, 2026, 5, 13)] // recent watermark → clamp to trailing window
    [InlineData(2026, 5, 13, 2026, 5, 13)] // watermark == trailing start → trailing start
    [InlineData(2026, 4, 17, 2026, 4, 17)] // watermark older than trailing → resume at watermark
    public void ComputeWindowStart_WithWatermark_ResumesButNeverBelowTrailingWindow(
        int wYear,
        int wMonth,
        int wDay,
        int eYear,
        int eMonth,
        int eDay
    )
    {
        var result = Holdings13FRealtimeWorker.ComputeWindowStart(
            new DateOnly(2026, 5, 27),
            new DateOnly(wYear, wMonth, wDay),
            trailingDays: 14,
            firstRunLookbackDays: 0
        );

        result.Should().Be(new DateOnly(eYear, eMonth, eDay));
    }

    [Fact]
    public void ComputeWindowStart_NoWatermark_GoesBackFirstRunLookbackInclusive()
    {
        // Cold start: a 10-day lookback covers today back through today-9 (10 days inclusive).
        var result = Holdings13FRealtimeWorker.ComputeWindowStart(
            new DateOnly(2026, 5, 27),
            watermark: null,
            trailingDays: 14,
            firstRunLookbackDays: 10
        );

        result.Should().Be(new DateOnly(2026, 5, 18));
    }
}
