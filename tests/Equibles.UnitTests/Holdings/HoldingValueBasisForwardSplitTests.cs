using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingValueBasisForwardSplitTests
{
    [Fact]
    public void TryResolveShareCountFactor_ForwardSplitAfterReportDate_RestoresTheUnderstatedValue()
    {
        // The same units error in the other direction, and the far more widespread one: a forward
        // split makes every position filed before it read too small, not too large, so nothing
        // looks obviously wrong and it hides. NVDA's 10:1 split on 2024-06-10 rewrote its history
        // so the 2023-12-29 close reads $49.52 instead of the $495.22 the shares actually traded
        // at, while filers' 2023 counts stayed pre-split — understating roughly 4,200 NVDA
        // positions per quarter by exactly tenfold, and with them every AUM ranking and
        // most-held-stock table built on those values.
        //
        // The risk this catches: treating the reverse-split case as the whole bug and gating the
        // restatement on the factor being less than one.
        var splits = new List<StockSplit>
        {
            new()
            {
                EffectiveDate = new DateOnly(2024, 6, 10),
                Numerator = 10m,
                Denominator = 1m,
                PriceAdjustmentAppliedTime = new DateTime(2024, 6, 11, 0, 0, 0, DateTimeKind.Utc),
            },
        };

        var resolved = HoldingValueBasis.TryResolveShareCountFactor(
            new DateOnly(2023, 12, 31),
            splits,
            null,
            "TEST",
            out var factor
        );

        resolved.Should().BeTrue();
        factor.Should().Be(10m);

        // 900,000 shares as filed. Priced raw against the restated close this is $44,568,000;
        // restated first it is the ~$445.7M the filer actually held.
        var derived = (long)(900_000L * factor * 49.52m);

        derived.Should().Be(445_680_000L);
    }
}
