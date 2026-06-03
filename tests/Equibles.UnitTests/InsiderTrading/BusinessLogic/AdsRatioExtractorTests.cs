using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

// Cases are drawn from real Form 4 footnote styles across many ADS/ADR issuers
// (SaverOne, Adaptimmune, GW Pharmaceuticals, Avadel, Moatable, Taiwan
// Semiconductor) plus the foreign-issuer ordinary-shares-direct negative class
// (Freightos). The extractor must restate ONLY the unambiguous mismatch — an
// ordinary-share count priced per ADS — and leave every other style alone.
public class AdsRatioExtractorTests
{
    public static IEnumerable<object[]> Cases()
    {
        // SaverOne (SVRE): ordinary count priced per ADS, comma-grouped ratio.
        // 2,501,582,400 / 43,200 = 57,907 (exact) → corrected.
        yield return new object[]
        {
            "Ordinary Shares",
            new[]
            {
                "The price reported is the price per American Depositary Share (\"ADS\") acquired in an open-market transaction on The Nasdaq Stock Market LLC. Each ADS represents 43,200 ordinary shares of the Issuer pursuant to the ADS ratio effective February 25, 2026. The Reporting Person acquired 57,907 ADSs on April 9, 2026 at $3.45, all per ADS, resulting in the underlying ordinary shares reported.",
            },
            2_501_582_400L,
            43_200,
        };

        // Adaptimmune (ADAP): ordinary count priced per ADS, digit ratio. The
        // total-count clause ("...representing 33,931,740 Ordinary Shares") must
        // NOT be read as the ratio. 33,931,740 / 6 = 5,655,290 (exact).
        yield return new object[]
        {
            "Ordinary Shares",
            new[]
            {
                "These Ordinary Shares are held through American Depositary Shares (\"ADS\") of the Issuer. Each ADS represents 6 Ordinary Shares.",
                "The reporting persons sold 5,655,290 ADSs representing 33,931,740 Ordinary Shares.",
                "The price reported in Column 4 is the price per ADS sold by the reporting persons.",
            },
            33_931_740L,
            6,
        };

        // Number-word ratio ("twelve") with an explicit per-ADS price. 240 / 12 exact.
        yield return new object[]
        {
            "Ordinary Shares",
            new[]
            {
                "Price reported is per ADS.",
                "Each ADS represents twelve ordinary shares of the Issuer.",
            },
            240L,
            12,
        };

        // "five (5)" word-plus-parenthetical, "Common Shares". 500 / 5 exact.
        yield return new object[]
        {
            "Common Shares",
            new[]
            {
                "The reported price is the price of each ADS.",
                "Each American Depositary Share represents five (5) Common Shares.",
            },
            500L,
            5,
        };

        // ADR token (not "ADS") with a per-ADR price. 400 / 4 exact.
        yield return new object[]
        {
            "Ordinary Shares",
            new[]
            {
                "The price reported is per ADR.",
                "Each ADR represents 4 ordinary shares of the issuer.",
            },
            400L,
            4,
        };

        // "of the Company's" possessive between the ratio and "ordinary shares".
        // 5,000 / 1,000 exact.
        yield return new object[]
        {
            "Ordinary Shares",
            new[]
            {
                "Price reported is per ADS.",
                "Each ADS represents 1,000 of the Company's ordinary shares.",
            },
            5_000L,
            1_000,
        };

        // NEGATIVE — GW Pharmaceuticals: price already converted to per-ordinary.
        yield return new object[]
        {
            "Ordinary Shares",
            new[]
            {
                "Each ADS represents twelve ordinary shares of the Issuer.",
                "Converted from price per ADS. The price reported in Column 4 is a weighted average price per ordinary share ($140.0338 per ADS).",
            },
            93_468L,
            0,
        };

        // NEGATIVE — Avadel: the row is titled in ADS units, so count and price
        // are already consistent (and the ratio is 1 anyway).
        yield return new object[]
        {
            "ADSs",
            new[]
            {
                "The issuer's \"ADSs\" are American Depositary Shares, with each ADS representing one ordinary share, nominal value $0.01 per share, of the issuer; ADSs may be represented by American Depositary Receipts.",
            },
            70_000L,
            0,
        };

        // NEGATIVE — Moatable: per-ADS price + ordinary title, but the share
        // count is the ADS count (59,168 / 45 is not whole) → the arithmetic
        // gate refuses to divide, which would otherwise under-value the row.
        yield return new object[]
        {
            "Class A Ordinary Shares",
            new[]
            {
                "The reported price is the price of each ADS purchased, the price was paid in USD. Each ADS represents 45 Class A Ordinary Shares.",
            },
            59_168L,
            0,
        };

        // NEGATIVE — Taiwan Semiconductor: ratio footnote present, but the priced
        // row is per common share (FX-translated), not per ADS.
        yield return new object[]
        {
            "Common Shares (2330.TW)",
            new[]
            {
                "Each American Depositary Share represents five (5) Common Shares.",
                "The price was translated from the average purchase price of NT$1,837.2789 in New Taiwan dollars, at the rate of NT$31.748 to US$1.",
            },
            88L,
            0,
        };

        // NEGATIVE — Freightos: foreign issuer listing ordinary shares directly
        // (no ADS wrapper). Looks like the bug — ordinary title — but no ADS
        // footnote at all, so it must never be touched.
        yield return new object[]
        {
            "Ordinary Shares",
            new[]
            {
                "The transaction reported in this row consists of a sale-to-cover on behalf of the Reporting Person to cover tax liability for vesting of restricted share units.",
            },
            17_898L,
            0,
        };

        // NEGATIVE — Form 3 holding: no footnotes at all.
        yield return new object[]
        {
            "Ordinary Shares",
            System.Array.Empty<string>(),
            6_418_828_800L,
            0,
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TryGetOrdinarySharesPerAds_AcrossFilingStyles_MatchesExpectedRatio(
        string securityTitle,
        string[] notes,
        long shares,
        int expectedRatio
    )
    {
        var corrected = AdsRatioExtractor.TryGetOrdinarySharesPerAds(
            securityTitle,
            notes,
            shares,
            out var ratio
        );

        corrected.Should().Be(expectedRatio != 0);
        ratio.Should().Be(expectedRatio);
    }
}
