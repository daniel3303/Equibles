using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class AdsRatioExtractorAdverbBeforeRatioTests
{
    // Contract (TryParseRatio, AdsRatioExtractor.cs:179): "The ratio is the first
    // number within a token or two of 'represents'." Every other positive case in
    // the suite places the number immediately after "represents"; here an adverb
    // ("approximately") sits between them, so the ratio is the SECOND token after
    // "represents". With an ordinary-share title, a per-ADS price, and a count that
    // is an exact multiple of the ratio, the row must still be corrected with 12.
    [Fact]
    public void TryGetOrdinarySharesPerAds_AdverbBetweenRepresentsAndRatio_StillFindsRatio()
    {
        var corrected = AdsRatioExtractor.TryGetOrdinarySharesPerAds(
            "Ordinary Shares",
            new[]
            {
                "The reported price is per ADS.",
                "Each ADS represents approximately 12 ordinary shares of the Issuer.",
            },
            240L,
            out var ratio
        );

        corrected.Should().BeTrue();
        ratio.Should().Be(12);
    }
}
