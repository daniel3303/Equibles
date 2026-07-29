using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins <c>YahooPriceImportService.IsSameSplitBasis</c>, the guard that stops the volume resettle
/// from comparing two records of the same session that sit on different split bases.
///
/// The stored series and the feed really do disagree. ReconcilePendingSplits rewrites a split
/// stock's whole history onto the post-split basis while the feed keeps serving that window
/// as-traded, so the same session exists twice on two bases — observed on WLFC's 3:1 split.
///
/// The failure mode this prevents is specific to REVERSE splits: they leave the stored volume
/// SMALLER than the as-traded figure by the split ratio, so the served number always looks like a
/// settlement upgrade and would overwrite a correctly-adjusted row, inflating that stock's volume
/// history. Forward splits are only accidentally safe.
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
    public void IsSameSplitBasis_ForwardSplitBases_AreRejected()
    {
        // Real WLFC 2026-07-14 across its 3:1 split: the stored row had been reconciled onto the
        // post-split basis (72.3267) while the feed was still serving that session as-traded
        // (216.98). Comparing their volumes is meaningless.
        IsSameSplitBasis(72.3267m, 216.98m).Should().BeFalse();
    }

    [Fact]
    public void IsSameSplitBasis_ReverseSplitBases_AreRejected()
    {
        // The dangerous direction. A 1:25 reverse leaves the stored close 25x LARGER and the stored
        // volume 25x SMALLER than as-traded, so without this guard the served volume always reads
        // as an upgrade and overwrites a correct row.
        IsSameSplitBasis(8.05m, 0.322m).Should().BeFalse();
    }

    [Fact]
    public void IsSameSplitBasis_SameBasis_IsAccepted()
    {
        // The ordinary case the resettle exists for: an unsplit session, same close on both sides,
        // so the volumes describe the same thing and the higher one is a genuine settlement.
        IsSameSplitBasis(39.41m, 39.41m).Should().BeTrue();
    }

    [Fact]
    public void IsSameSplitBasis_RoundingGap_IsStillSameBasis()
    {
        // The stored close is a rounded numeric(18,4) while the feed carries full precision. That
        // gap must not read as a basis mismatch, or the resettle would silently stop correcting
        // anything — the guard would turn into an off switch.
        IsSameSplitBasis(39.41m, 39.409999847412109375m).Should().BeTrue();
    }

    [Fact]
    public void IsSameSplitBasis_SmallestRealSplitRatio_IsRejected()
    {
        // A 5:4 split moves the close 25% — the tightest real ratio, and still far outside the
        // tolerance. Pins that the tolerance cannot be widened into admitting a split.
        IsSameSplitBasis(80m, 100m).Should().BeFalse();
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
