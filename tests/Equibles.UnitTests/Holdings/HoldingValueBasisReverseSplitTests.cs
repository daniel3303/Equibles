using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingValueBasisReverseSplitTests
{
    [Fact]
    public void TryResolveShareCountFactor_ReverseSplitAfterReportDate_ValueMatchesTheFiledFigure()
    {
        // The production defect this pins, with the real numbers. Scion Asset Management's
        // Q2 2024 13F reported 633,959 BioAtla shares worth $868,524. BioAtla ran a 1:50 reverse
        // split on 2026-04-06; the split-price reconciliation then rewrote the stock's whole
        // stored price history onto the post-split basis, so the 2024-06-30 close became $68.50
        // (fifty times the $1.37 the shares actually traded at). The filing was re-imported after
        // that, multiplying an as-filed count by a restated price, and the position was published
        // at $43,426,191 — fifty times its real size, and enough to make BioAtla ~48% of that
        // quarter's cloned portfolio.
        //
        // The risk this catches: anyone "simplifying" the derivation back to shares × price, or
        // reaching for SplitAdjustment.PriceFactor (which restates the price, the wrong direction
        // when the stored price is already current). Either way the 50x returns silently, and
        // nothing downstream can tell a real $43M position from this one.
        var splits = new List<StockSplit>
        {
            new()
            {
                EffectiveDate = new DateOnly(2026, 4, 6),
                Numerator = 1m,
                Denominator = 50m,
                PriceAdjustmentAppliedTime = new DateTime(2026, 4, 7, 0, 0, 0, DateTimeKind.Utc),
            },
        };

        var resolved = HoldingValueBasis.TryResolveShareCountFactor(
            new DateOnly(2024, 6, 30),
            splits,
            null,
            "TEST",
            out var factor
        );

        resolved.Should().BeTrue();
        factor.Should().Be(0.02m);

        // 633,959 shares restated onto today's basis, priced at the stored (restated) close.
        // The filer reported $868,524; the derivation lands one dollar under it, purely from
        // truncating the product to whole dollars. The point is the order of magnitude: without
        // the factor this is $43,426,191.
        var derived = (long)(633_959L * factor * 68.50m);

        derived.Should().Be(868_523L);
    }
}
