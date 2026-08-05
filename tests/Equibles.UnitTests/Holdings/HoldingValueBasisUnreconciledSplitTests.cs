using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingValueBasisUnreconciledSplitTests
{
    [Fact]
    public void TryResolveShareCountFactor_SplitAwaitingPriceAdjustment_RefusesToResolve()
    {
        // A split is captured as soon as the price sync sees it, but the stored history is only
        // rewritten onto the new basis when the reconciliation pass gets to that stock (capped per
        // cycle, so the gap is hours to days). In between, the series is half old basis and half
        // new — restating the count then applies the ratio a second time, turning a 10:1 split
        // into a 100x error, which is worse than the bug being fixed.
        //
        // The risk this catches: someone reading PriceAdjustmentAppliedTime as bookkeeping and
        // dropping the check to "simplify" the factor into a plain SplitAdjustment call. Nothing
        // would fail loudly; a handful of positions per cycle would just be wrong by a square.
        var splits = new List<StockSplit>
        {
            new()
            {
                EffectiveDate = new DateOnly(2026, 4, 6),
                Numerator = 1m,
                Denominator = 50m,
                PriceAdjustmentAppliedTime = null,
            },
        };

        var resolved = HoldingValueBasis.TryResolveShareCountFactor(
            new DateOnly(2024, 6, 30),
            splits,
            null,
            "TEST",
            null,
            out var factor
        );

        resolved.Should().BeFalse();
        factor.Should().Be(1m, "an unusable factor must be inert, never silently applied");
    }

    [Fact]
    public void TryResolveShareCountFactor_UnappliedSplitBeforeReportDate_StillResolves()
    {
        // An unreconciled split only makes the basis ambiguous for reports filed BEFORE it. A
        // position reported after the split already counts post-split shares and needs no
        // restatement, so refusing it would park perfectly valuable rows as pending — the newest
        // quarter's rows, which are the ones every live surface reads.
        var splits = new List<StockSplit>
        {
            new()
            {
                EffectiveDate = new DateOnly(2024, 6, 10),
                Numerator = 10m,
                Denominator = 1m,
                PriceAdjustmentAppliedTime = null,
            },
        };

        var resolved = HoldingValueBasis.TryResolveShareCountFactor(
            new DateOnly(2025, 3, 31),
            splits,
            null,
            "TEST",
            null,
            out var factor
        );

        resolved.Should().BeTrue();
        factor.Should().Be(1m);
    }
}
