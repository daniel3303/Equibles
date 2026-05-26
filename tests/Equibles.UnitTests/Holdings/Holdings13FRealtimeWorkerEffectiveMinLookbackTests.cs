using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins EffectiveMinLookback: the realtime lookback floor never drops below the
/// gap to the latest completed quarter end, so a fresh/empty ProcessedDataSet
/// (or the startup backfill race) still covers the current filing season rather
/// than collapsing to the flat 7-day minimum (GH-2206).
/// </summary>
public class Holdings13FRealtimeWorkerEffectiveMinLookbackTests
{
    [Fact]
    public void EffectiveMinLookback_MidSeason_FloorsToQuarterEndGap()
    {
        // 2026-05-26 is 56 days after the 2026-03-31 quarter end — the window
        // must reach back across the whole Q1 2026 filing season, not 7 days.
        var result = Holdings13FRealtimeWorker.EffectiveMinLookback(
            new DateOnly(2026, 5, 26),
            minLookbackDays: 7
        );

        result.Should().Be(56);
    }

    [Fact]
    public void EffectiveMinLookback_JustAfterQuarterEnd_KeepsMinimum()
    {
        // 2 days into a new quarter the gap is smaller than the floor, so the
        // configured minimum still wins.
        var result = Holdings13FRealtimeWorker.EffectiveMinLookback(
            new DateOnly(2026, 4, 2),
            minLookbackDays: 7
        );

        result.Should().Be(7);
    }
}
