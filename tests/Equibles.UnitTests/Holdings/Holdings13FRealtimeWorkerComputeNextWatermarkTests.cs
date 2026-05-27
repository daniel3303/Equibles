using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins ComputeNextWatermark (GH-2241): the watermark advances to today only
/// when every day swept cleanly; a failed/throttled day holds it back to the
/// day before the earliest failure so the gap is re-swept next cycle rather
/// than being silently skipped past.
/// </summary>
public class Holdings13FRealtimeWorkerComputeNextWatermarkTests
{
    [Fact]
    public void ComputeNextWatermark_NoFailure_ReturnsToday()
    {
        var result = Holdings13FRealtimeWorker.ComputeNextWatermark(
            new DateOnly(2026, 5, 27),
            earliestFailedDate: null
        );

        result.Should().Be(new DateOnly(2026, 5, 27));
    }

    [Fact]
    public void ComputeNextWatermark_FailedDay_ReturnsDayBeforeEarliestFailure()
    {
        var result = Holdings13FRealtimeWorker.ComputeNextWatermark(
            new DateOnly(2026, 5, 27),
            earliestFailedDate: new DateOnly(2026, 5, 20)
        );

        result.Should().Be(new DateOnly(2026, 5, 19));
    }
}
