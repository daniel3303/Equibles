using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class AdsRatioExtractorRatioOfOneTests
{
    [Fact]
    public void TryGetOrdinarySharesPerAds_OneToOneRatioPricedPerAds_LeavesRowUntouched()
    {
        // Contract: a 1:1 ADS issuer already has Shares (ordinary) == Shares (ADS),
        // so Shares x Price (per ADS) is correct and must NOT be rescaled. The
        // docstring requires "a ratio above 1"; here the title is ordinary (not the
        // ADS itself) and the price is per ADS, so the only thing barring a
        // correction is the ratio of exactly one.
        var corrected = AdsRatioExtractor.TryGetOrdinarySharesPerAds(
            "Ordinary Shares",
            new[]
            {
                "The reported price is per ADS.",
                "Each ADS represents one ordinary share of the Issuer.",
            },
            70_000L,
            out var ratio
        );

        corrected.Should().BeFalse();
        ratio.Should().Be(0);
    }
}
