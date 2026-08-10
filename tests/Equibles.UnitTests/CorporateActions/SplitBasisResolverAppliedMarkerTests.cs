using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.UnitTests.CorporateActions;

public class SplitBasisResolverAppliedMarkerTests
{
    [Theory]
    [InlineData(10, false, 1)]
    [InlineData(12, false, 1)]
    [InlineData(13, true, 2)]
    public void TryResolveFactor_RequiresPostEffectiveUtcMarker(
        int appliedDay,
        bool expectedResolved,
        int expectedFactor
    )
    {
        var split = new StockSplit
        {
            PriceSeriesTicker = "AAPL",
            EffectiveDate = new DateOnly(2026, 8, 12),
            Numerator = 2m,
            Denominator = 1m,
            PriceAdjustmentAppliedTime = new DateTime(
                2026,
                8,
                appliedDay,
                12,
                0,
                0,
                DateTimeKind.Utc
            ),
        };

        var resolved = SplitBasisResolver.TryResolveFactor(
            new DateOnly(2026, 8, 1),
            [split],
            listedTicker: null,
            primaryTicker: "AAPL",
            secondaryTickers: [],
            out var factor
        );

        resolved.Should().Be(expectedResolved);
        factor.Should().Be(expectedFactor);
    }
}
