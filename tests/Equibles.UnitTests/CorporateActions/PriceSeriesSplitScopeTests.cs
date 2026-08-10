using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.UnitTests.CorporateActions;

public class PriceSeriesSplitScopeTests
{
    private static readonly DateOnly LegacyDate = new(2025, 1, 2);
    private static readonly DateOnly PrimaryDate = new(2025, 2, 3);
    private static readonly DateOnly SecondaryDate = new(2025, 3, 4);

    [Fact]
    public void ForListing_Primary_IncludesExactAndLegacyNullAttribution()
    {
        var result = PriceSeriesSplitScope.ForListing(
            Splits(),
            primaryTicker: "BRK-A",
            listedTicker: "brk-a"
        );

        result.Select(split => split.EffectiveDate).Should().Equal(LegacyDate, PrimaryDate);
    }

    [Fact]
    public void ForListing_Secondary_IncludesOnlyItsExactAttribution()
    {
        var result = PriceSeriesSplitScope.ForListing(
            Splits(),
            primaryTicker: "BRK-A",
            listedTicker: "BRK-B"
        );

        result.Select(split => split.EffectiveDate).Should().Equal(SecondaryDate);
    }

    private static StockSplit[] Splits() =>
        [
            new StockSplit { PriceSeriesTicker = null, EffectiveDate = LegacyDate },
            new StockSplit { PriceSeriesTicker = "BRK-A", EffectiveDate = PrimaryDate },
            new StockSplit { PriceSeriesTicker = "BRK-B", EffectiveDate = SecondaryDate },
            new StockSplit
            {
                PriceSeriesTicker = "BRK-C",
                EffectiveDate = new DateOnly(2025, 4, 5),
            },
        ];
}
