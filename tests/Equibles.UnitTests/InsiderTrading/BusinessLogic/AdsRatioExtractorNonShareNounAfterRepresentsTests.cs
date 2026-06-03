using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class AdsRatioExtractorNonShareNounAfterRepresentsTests
{
    // Contract (TryParseRatio, AdsRatioExtractor.cs:165-169): the ratio number must
    // be "confirmed by the ordinary/common-share noun that follows so an unrelated
    // number isn't read." When an "ADS represents N ..." clause is followed by a
    // non-share noun (here "10 votes ..."), the number must NOT be taken as the
    // ratio — the parser gives up on that anchor instead of mis-reading it. Every
    // positive case in the suite places "ordinary/common shares" right after the
    // number, so this rejection path is unexercised. Even with a per-ADS price and
    // an ordinary-share title, the missing share-noun confirmation must leave the
    // row untouched (corrected = false, ratio = 0).
    [Fact]
    public void TryGetOrdinarySharesPerAds_NumberAfterRepresentsIsNotAShareCount_LeavesRowUntouched()
    {
        var corrected = AdsRatioExtractor.TryGetOrdinarySharesPerAds(
            "Ordinary Shares",
            new[]
            {
                "The reported price is per ADS.",
                "Each ADS represents 10 votes at any general meeting of the Company.",
            },
            100L,
            out var ratio
        );

        corrected.Should().BeFalse();
        ratio.Should().Be(0);
    }
}
