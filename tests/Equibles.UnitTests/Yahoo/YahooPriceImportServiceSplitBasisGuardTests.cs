using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins <c>YahooPriceImportService.IsSameSplitBasis</c>, the guard that stops bar resettlement
/// from comparing two records of the same session that sit on different split bases.
///
/// The stored series and the feed disagree in BOTH orderings, so the guard must be
/// direction-agnostic. Pre-reconcile — the window every split passes through, because the split is
/// captured at the end of the same cycle whose reconcile pass already ran — the stored rows are
/// as-traded while the feed serves them adjusted; on a forward split the adjusted volume is
/// ratio-times larger and reads as a settlement upgrade. Post-reconcile the stored rows are
/// adjusted while the feed can go back to serving as-traded (observed on WLFC); on a reverse split
/// the as-traded volume is ratio-times larger and reads as an upgrade. Either way the "upgrade"
/// would put a wrong-basis volume under the stored close, inflating history by the split ratio.
/// </summary>
public class YahooPriceImportServiceSplitBasisGuardTests
{
    private static readonly MethodInfo IsSameSplitBasisMethod =
        typeof(YahooPriceImportService).GetMethod(
            "IsSameSplitBasis",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static bool IsSameSplitBasis(decimal storedClose, decimal fetchedClose) =>
        (bool)IsSameSplitBasisMethod.Invoke(null, [storedClose, fetchedClose]);

    [Fact]
    public void IsSameSplitBasis_StoredAdjustedServedAsTraded_IsRejected()
    {
        // Real WLFC 2026-07-14 across its 3:1 split, post-reconcile: the stored row had been
        // reconciled onto the adjusted basis (72.3267) while the feed went back to serving the
        // session as-traded (216.98). Comparing their volumes is meaningless.
        IsSameSplitBasis(72.3267m, 216.98m).Should().BeFalse();
    }

    [Fact]
    public void IsSameSplitBasis_StoredAsTradedServedAdjusted_IsRejected()
    {
        // The same WLFC session in the PRE-reconcile ordering — the window every split passes
        // through. The stored row is still as-traded (216.98) while the feed already serves it
        // adjusted (72.3267), whose volume is 3x larger and would read as a settlement upgrade.
        // This ordering is why the guard cannot be narrowed to reverse splits only.
        IsSameSplitBasis(216.98m, 72.3267m).Should().BeFalse();
    }

    [Fact]
    public void IsSameSplitBasis_ReverseSplitBases_AreRejected()
    {
        // A 1:25 reverse in both orderings. Whichever side holds the as-traded close, its volume
        // is 25x the adjusted figure — an automatic "upgrade" without the guard.
        IsSameSplitBasis(8.05m, 0.322m).Should().BeFalse();
        IsSameSplitBasis(0.322m, 8.05m).Should().BeFalse();
    }

    [Fact]
    public void IsSameSplitBasis_SameBasis_IsAccepted()
    {
        // The ordinary case the resettle exists for: an unsplit session, same close on both sides,
        // so the volumes describe the same thing and the higher one is a genuine settlement.
        IsSameSplitBasis(39.41m, 39.41m).Should().BeTrue();
    }

    [Fact]
    public void IsSameSplitBasis_MinorRevision_IsStillSameBasis()
    {
        // Both closes are rounded to 4 decimals at ingest, so a same-basis pair differs only when
        // the feed genuinely revised the close a little. A one-tick revision must not read as a
        // basis mismatch, or the guard would silently turn the resettle into an off switch.
        IsSameSplitBasis(39.4100m, 39.4101m).Should().BeTrue();
    }

    [Fact]
    public void IsSameSplitBasis_ToleranceBoundary_IsPinned()
    {
        // 1% relative plus one tick of absolute headroom: at a stored close of 100 the cut sits
        // at 1.0001. Pins the boundary from both sides so the tolerance can neither silently
        // widen toward the split ratios nor collapse into exact equality.
        IsSameSplitBasis(100m, 101m).Should().BeTrue();
        IsSameSplitBasis(100m, 101.01m).Should().BeFalse();
    }

    [Fact]
    public void IsSameSplitBasis_SubCentOneTickRevision_IsStillSameBasis()
    {
        // The tick headroom's reason to exist: below $0.005 a single last-digit tick exceeds 1%
        // of the price, so a purely relative tolerance would freeze the resettle out of the OTC
        // tail forever. One tick must pass; the smallest split ratio at any price still fails.
        IsSameSplitBasis(0.0050m, 0.0051m).Should().BeTrue();
    }

    [Fact]
    public void IsSameSplitBasis_RealSplitRatios_AreRejected()
    {
        // The split ratios Yahoo emits for real splits sit far outside the tolerance: 5:4 moves
        // the close 25%, 21:20 (a 5% stock dividend) 4.76%. Pinned so the tolerance can never be
        // widened into admitting a split.
        IsSameSplitBasis(80m, 100m).Should().BeFalse();
        IsSameSplitBasis(100m, 95.2381m).Should().BeFalse();
    }

    [Fact]
    public void IsSameSplitBasis_TinyStockDividendRatio_IsInsideTheBound()
    {
        // Deliberate, documented bound: a 1% stock dividend recorded as a 101:100 split moves the
        // close 0.99% — inside the tolerance — so its volume comparison proceeds and the error is
        // bounded at ~1%, negligible against the 10-29% unsettled shortfall being repaired. If
        // this pin breaks because the tolerance tightened, sub-cent stocks likely broke too.
        IsSameSplitBasis(100m, 99.0099m).Should().BeTrue();
    }

    [Fact]
    public void IsSameSplitBasis_NonPositiveClose_IsRejected()
    {
        // Nothing to compare against, so the basis is unproven rather than matching. A zero stored
        // close would also collapse the relative tolerance to an exact-equality test.
        IsSameSplitBasis(0m, 39.41m).Should().BeFalse();
        IsSameSplitBasis(39.41m, 0m).Should().BeFalse();
        IsSameSplitBasis(-39.41m, -39.41m).Should().BeFalse();
    }
}
