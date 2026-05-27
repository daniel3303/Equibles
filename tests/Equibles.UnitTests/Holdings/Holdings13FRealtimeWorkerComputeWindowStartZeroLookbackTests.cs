using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Sibling to <see cref="Holdings13FRealtimeWorkerComputeWindowStartTests"/>.
/// The cold-start arm normalises a zero (or smaller) <c>firstRunLookbackDays</c>
/// via <c>Math.Max(firstRunLookbackDays, 1) - 1</c> so the window degenerates to
/// today alone, never to tomorrow. Without the floor, a misconfigured option
/// passing 0 would compute <c>today.AddDays(-(0 - 1)) = today.AddDays(1)</c> —
/// a future-dated window start that scans nothing and silently skips the very
/// day the operator wanted swept. Pin the lower bound at today on the
/// equals-zero input.
/// </summary>
public class Holdings13FRealtimeWorkerComputeWindowStartZeroLookbackTests
{
    [Fact]
    public void ComputeWindowStart_NoWatermarkZeroFirstRunLookback_ClampsToTodayNotTomorrow()
    {
        var result = Holdings13FRealtimeWorker.ComputeWindowStart(
            today: new DateOnly(2026, 5, 27),
            watermark: null,
            trailingDays: 14,
            firstRunLookbackDays: 0
        );

        result.Should().Be(new DateOnly(2026, 5, 27));
    }
}
