using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins the two pure rules behind the volume resettle: which stored bars are still re-read
/// (<c>ResettleWindowStart</c>) and which fetched figure is allowed to replace a stored one
/// (<c>IsVolumeUpgrade</c>).
///
/// The bug they guard: a daily bar becomes storable four hours after the US close, when the feed
/// is still serving an unsettled volume that it revises upward overnight. OHLC is correct from the
/// start; volume is routinely a quarter short for names whose shares settle late. The importer is
/// otherwise insert-only, so without the resettle that first short figure is permanent.
/// </summary>
public class YahooPriceImportServiceVolumeResettleTests
{
    private static readonly MethodInfo IsVolumeUpgradeMethod =
        typeof(YahooPriceImportService).GetMethod(
            "IsVolumeUpgrade",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static readonly MethodInfo ResettleWindowStartMethod =
        typeof(YahooPriceImportService).GetMethod(
            "ResettleWindowStart",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static bool IsVolumeUpgrade(long stored, long fetched) =>
        (bool)IsVolumeUpgradeMethod.Invoke(null, [stored, fetched]);

    private static DateOnly ResettleWindowStart(DateOnly today, int windowDays) =>
        (DateOnly)ResettleWindowStartMethod.Invoke(null, [today, windowDays]);

    private static readonly DateOnly Today = new(2026, 7, 29);

    [Fact]
    public void IsVolumeUpgrade_SettledFigureAboveStored_IsAccepted()
    {
        // The case that motivated the fix: KRC's 2026-07-27 bar stored 1,183,039 shares hours
        // after the close, while the settled figure was 1,573,891 — a 24.8% shortfall that the
        // insert-only importer would otherwise have kept forever.
        IsVolumeUpgrade(1_183_039, 1_573_891).Should().BeTrue();
    }

    [Fact]
    public void IsVolumeUpgrade_FigureBelowStored_IsRejected()
    {
        // Settled volume only accrues, so a lower reading is a degraded response — a partial
        // re-serve or a venue dropping out — never a correction. Accepting it would let a flaky
        // feed walk a good figure back down, which is worse than the shortfall being fixed.
        IsVolumeUpgrade(1_573_891, 1_183_039).Should().BeFalse();
    }

    [Fact]
    public void IsVolumeUpgrade_UnchangedFigure_IsRejected()
    {
        // The steady-state case: an already-settled bar re-read on the next sync must not mark the
        // row dirty, or every stock would write every session's row back on every cycle.
        IsVolumeUpgrade(1_573_891, 1_573_891).Should().BeFalse();
    }

    [Fact]
    public void ResettleWindowStart_DefaultWindow_ReachesBackOverAWeekend()
    {
        // The default of 5 days must cover the previous session even across a weekend: syncing on
        // a Wednesday has to still reach the preceding Friday's bar, which is the oldest one whose
        // volume could have been captured unsettled.
        ResettleWindowStart(Today, 5).Should().Be(new DateOnly(2026, 7, 24));
    }

    [Fact]
    public void ResettleWindowStart_WidenedWindow_ReachesBackFurther()
    {
        // Raising the setting is how a longer stretch of stored volumes gets repaired: the window
        // only widens a fetch that was already happening, so a wider setting buys a longer repair
        // at no extra upstream calls.
        ResettleWindowStart(Today, 120).Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void ResettleWindowStart_ZeroOrNegativeWindow_ClampsToToday()
    {
        // A misconfigured zero or negative window must degrade to "today only" — which the
        // settled-bar guard then empties — rather than flipping the sign and reaching FORWARD, or
        // being read as "no clamp" and dragging the fetch back over the stock's whole series.
        ResettleWindowStart(Today, 0).Should().Be(Today);
        ResettleWindowStart(Today, -30).Should().Be(Today);
    }
}
