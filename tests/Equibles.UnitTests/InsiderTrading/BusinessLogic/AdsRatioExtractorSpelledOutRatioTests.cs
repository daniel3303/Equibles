using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

// Ratios above twenty are spelled out too ("sixty", "thirty-five", "one hundred"); a
// multi-word number must be read whole, never as its leading word.
public class AdsRatioExtractorSpelledOutRatioTests
{
    // 51Talk (COE) Form 4 footnotes as filed: an ordinary-share count priced per ADS.
    private const string CoeRatioNote =
        "The Class A ordinary shares are held in the form of American depositary shares (\"ADS\"). Each ADS represents sixty Class A ordinary shares.";

    private const string CoePriceNote =
        "The price reported in Column 4 is a weighted average price of ADS. The reporting person executed a trade order through a broker-dealer which resulted in multiple same-day, same-way open market purchases, with prices ranging from $20.26 to $21.24 per ADS. The reporting person has reported these purchases on an aggregate basis using the weighted average price, rounded to the nearest cent, for the transactions.";

    [Fact]
    public void TryGetOrdinarySharesPerAds_CoeSixtyRatioPricedPerAds_ReturnsSixty()
    {
        var corrected = AdsRatioExtractor.TryGetOrdinarySharesPerAds(
            "Class A Ordinary Share, par value US$0.0001",
            new[] { CoeRatioNote, CoePriceNote },
            35_400L,
            out var ratio
        );

        corrected.Should().BeTrue();
        ratio.Should().Be(60);
    }

    [Fact]
    public void TryGetOrdinarySharesPerAds_CoeRatioWithoutPerAdsPrice_LeavesRowUntouched()
    {
        // COE's June filings state the ratio but never say the price is per ADS, so the
        // unit mismatch is not proven and the row keeps its filed price.
        var corrected = AdsRatioExtractor.TryGetOrdinarySharesPerAds(
            "Class A Ordinary Share, par value US$0.0001",
            new[] { CoeRatioNote },
            60_000L,
            out var ratio
        );

        corrected.Should().BeFalse();
        ratio.Should().Be(0);
    }

    [Theory]
    [InlineData("Each ADS represents thirty-five ordinary shares of the Issuer.", 7_000L, 35)]
    [InlineData("Each ADS represents twenty five ordinary shares of the Issuer.", 2_525L, 25)]
    [InlineData(
        "Each American depositary share represents one hundred (100) Class A common shares.",
        10_000L,
        100
    )]
    [InlineData(
        "Each ADS represents one hundred and twenty ordinary shares of the Issuer.",
        2_400L,
        120
    )]
    public void TryGetOrdinarySharesPerAds_CompoundSpelledOutRatio_ReadsWholeNumber(
        string ratioNote,
        long shares,
        int expectedRatio
    )
    {
        var corrected = AdsRatioExtractor.TryGetOrdinarySharesPerAds(
            "Ordinary Shares",
            new[] { ratioNote, "The price reported is the price per ADS." },
            shares,
            out var ratio
        );

        corrected.Should().BeTrue();
        ratio.Should().Be(expectedRatio);
    }
}
