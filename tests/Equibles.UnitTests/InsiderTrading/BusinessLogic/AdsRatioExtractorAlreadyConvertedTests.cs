using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class AdsRatioExtractorAlreadyConvertedTests
{
    [Fact]
    public void TryGetOrdinarySharesPerAds_PriceAlreadyConvertedToPerOrdinary_LeavesRowUntouched()
    {
        // Contract: a per-ADS price + ordinary title + a valid ratio still must NOT
        // be corrected when a footnote says the price was ALREADY restated to a
        // per-ordinary figure — correcting again would double-divide and under-value
        // the row. Existing cases cover the title/ratio/multiple guards but not this
        // "already converted" guard. Oracle derived from the doc-comment + the
        // AlreadyConvertedMarkers contract, before reading the gating logic.
        var notes = new[]
        {
            "The price reported is per ADS. Each ADS represents 12 ordinary shares; "
                + "the per-share amount shown was converted from the price per ADS divided by the ratio.",
        };

        var corrected = AdsRatioExtractor.TryGetOrdinarySharesPerAds(
            "Ordinary Shares",
            notes,
            1_200L, // exact multiple of 12, so only the already-converted guard can refuse
            out var ratio
        );

        corrected.Should().BeFalse();
        ratio.Should().Be(0);
    }
}
