using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class AdsRatioExtractorPluralTotalClauseTests
{
    // Contract guarantee: a plural "<N> ADSs representing <M> Ordinary Shares"
    // total-count clause must NEVER be read as the per-ADS ratio — only the
    // singular "ADS represents N ordinary shares" form anchors. Existing cases
    // always pair this plural clause with a real singular ratio clause; this
    // isolates a filing whose ONLY ratio-shaped text is the plural total clause.
    // If the total (33,931,740) were misread as the ratio, the row would be
    // mis-valued by ~34M× — so the row must be left untouched.
    [Fact]
    public void TryGetOrdinarySharesPerAds_PluralAdsTotalCountClauseOnly_LeavesRowUntouched()
    {
        var notes = new[]
        {
            "The price reported is the price per ADS.",
            "The reporting person sold 5,655,290 ADSs representing 33,931,740 Ordinary Shares.",
        };

        var corrected = AdsRatioExtractor.TryGetOrdinarySharesPerAds(
            "Ordinary Shares",
            notes,
            33_931_740L,
            out var ratio
        );

        corrected.Should().BeFalse();
        ratio.Should().Be(0);
    }
}
