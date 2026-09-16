using Equibles.EquityMarkets.Data.Catalog;
using Equibles.Integrations.Esma;
using Equibles.Integrations.Euronext;
using Equibles.Integrations.NasdaqNordic;

namespace Equibles.UnitTests.EquityMarkets;

public class EquityMarketCatalogTests
{
    [Fact]
    public void EveryMarket_HasUniqueCodeDisjointVenuesAndACompleteYahooIdentity()
    {
        EquityMarketCatalog.All.Select(market => market.Code).Should().OnlyHaveUniqueItems();
        EquityMarketCatalog
            .All.SelectMany(market => market.MarketIdentifierCodes)
            .Should()
            .OnlyHaveUniqueItems();
        foreach (var market in EquityMarketCatalog.All)
        {
            market.Code.Should().MatchRegex("^[a-z]+(-[a-z]+)*$");
            market.CountryCode.Should().MatchRegex("^[A-Z]{2}$");
            market.Currency.Should().MatchRegex("^[A-Z]{3}$");
            market.MarketIdentifierCodes.Should().NotBeEmpty();
            market
                .MarketIdentifierCodes.Should()
                .AllSatisfy(mic => mic.Should().MatchRegex("^[A-Z0-9]{4}$"));
            market.YahooSuffix.Should().StartWith(".");
            market.YahooExchangeCode.Should().NotBeNullOrWhiteSpace();
            TimeZoneInfo.FindSystemTimeZoneById(market.TimeZoneId).Should().NotBeNull();
            market.SessionOpen.Should().BeBefore(market.SessionClose);
            market.SessionClose.Should().BeOnOrBefore(market.ClosingAuctionEnd);
            EquityQuotationUnits.TryResolve(market.Currency, out _, out _).Should().BeTrue();
        }
    }

    [Fact]
    public void VenueCodes_PlaceEveryHomeVenueInExactlyOneMarketAndFileXetraBySegment()
    {
        EquityMarketCatalog
            .All.SelectMany(market => market.HomeVenueCodes)
            .Should()
            .OnlyHaveUniqueItems("a venue cannot be the home of two markets");
        EquityMarketCatalog
            .All.Select(market => market.CountryCode)
            .Should()
            .OnlyHaveUniqueItems(
                "the gate's competent-authority escape assumes one catalog market per country"
            );
        foreach (var market in EquityMarketCatalog.All)
        {
            market.FirdsVenueCodes.Should().NotBeEmpty();
            market
                .FirdsVenueCodes.Should()
                .AllSatisfy(mic => mic.Should().MatchRegex("^[A-Z0-9]{4}$"));
            market.HomeVenueCodes.Should().Contain(market.MarketIdentifierCodes);
            market.HomeVenueCodes.Should().Contain(market.FirdsVenueCodes);
            if (!SegmentFiledMarkets.Contains(market.Code))
            {
                market.FirdsVenueCodes.Should().Equal(market.MarketIdentifierCodes);
                market.HomeVenueCodes.Should().Equal(market.MarketIdentifierCodes);
            }
        }
        foreach (var market in EquityMarketCatalog.All)
            market
                .FirdsAuthority.Should()
                .Be(
                    market.CountryCode == "GB"
                        ? FcaFirdsClient.AuthorityCode
                        : EsmaFirdsClient.AuthorityCode,
                    "a UK venue's universe is the FCA's register, every EEA venue's is ESMA's"
                );
        var xetra = EquityMarketCatalog.TryGet("xetra");
        xetra.FirdsVenueCodes.Should().Equal("XETA", "XETB", "XETS");
        xetra.HomeVenueCodes.Should().Contain(["XETR", "XFRA", "FRAA", "FRAB"]);
        xetra.HomeVenueCodes.Should().NotContain(["MUNB", "STUB", "XGAT", "WBAH"]);
        xetra
            .IsFirdsVenue("XETR")
            .Should()
            .BeFalse("FIRDS never files a line under the operating MIC");
        xetra.IsHomeVenue("FRAA").Should().BeTrue();
        xetra.IsHomeVenue(null).Should().BeFalse();
    }

    // The markets FIRDS files under segment codes rather than the operating MIC alone.
    private static readonly string[] SegmentFiledMarkets =
    [
        "xetra",
        "nasdaq-stockholm",
        "nasdaq-helsinki",
        "nasdaq-copenhagen",
        "bme",
    ];

    // A Nordic main-market line is filed on the lit book and its Nordic@Mid and Auction on Demand segments, a
    // First North line on the latter two and the SME growth-market code; Madrid adds its dark midpoint book.
    [Theory]
    [InlineData("nasdaq-stockholm", "XSTO,FNSE", "XSTO,DSTO,MSTO,FNSE,DNSE,MNSE,SSME")]
    [InlineData("nasdaq-helsinki", "XHEL,FNFI", "XHEL,DHEL,MHEL,FNFI,DNFI,MNFI,FSME")]
    [InlineData("nasdaq-copenhagen", "XCSE,FNDK", "XCSE,DCSE,MCSE,FNDK,DNDK,MNDK,DSME")]
    [InlineData("bme", "XMAD", "XMAD,DMAD")]
    [InlineData("gpw", "XWAR", "XWAR")]
    public void SegmentFiledMarkets_HomeEveryVenueCodeTheRegisterFilesThemUnder(
        string code,
        string mics,
        string venues
    )
    {
        var market = EquityMarketCatalog.TryGet(code);
        market.MarketIdentifierCodes.Should().Equal(mics.Split(','));
        market.FirdsVenueCodes.Should().Equal(venues.Split(','));
        market.HomeVenueCodes.Should().Equal(venues.Split(','));
        market.DirectorySource.Should().NotBeNull();
        market.FirdsAuthority.Should().Be(EsmaFirdsClient.AuthorityCode);
    }

    [Fact]
    public void DirectorySources_ServeEveryMarketAndLondonRunsLast()
    {
        EquityMarketCatalog
            .All.Should()
            .AllSatisfy(market => market.DirectorySource.Should().NotBeNull());
        EquityMarketCatalog
            .All.Last()
            .Code.Should()
            .Be(
                "lse",
                "the EEA directories hold an issuer's presentation before its London line arrives"
            );
        EquityMarketCatalog
            .All.Select(market => market.DirectorySource)
            .Distinct()
            .Should()
            .BeEquivalentTo(["euronext", "xetra", "nasdaq-nordic", "bme", "gpw", "lse"]);
    }

    [Fact]
    public void London_IsTheOneMarketTheUnitedKingdomAuthorityRecords()
    {
        var london = EquityMarketCatalog.TryGet("lse");
        london.FirdsAuthority.Should().Be("FCA");
        london.MarketIdentifierCodes.Should().Equal("XLON", "AIMX");
        london.FirdsVenueCodes.Should().Equal("XLON", "AIMX");
        london.HomeVenueCodes.Should().Equal("XLON", "AIMX");
        london.Currency.Should().Be("GBP");
        EquityMarketCatalog
            .All.Where(market => market.FirdsAuthority != "ESMA")
            .Select(market => market.Code)
            .Should()
            .Equal("lse");
    }

    [Fact]
    public void NasdaqMarkets_AgreeWithTheNasdaqNordicDefinitions()
    {
        var catalogued = EquityMarketCatalog
            .All.Where(market => market.DirectorySource == "nasdaq-nordic")
            .ToList();
        catalogued.Should().HaveCount(NasdaqNordicMarket.All.Count);
        foreach (var market in catalogued)
        {
            var nasdaq = NasdaqNordicMarket.FromSlug(market.Code["nasdaq-".Length..]);
            nasdaq.Should().NotBeNull(market.Code);
            market
                .MarketIdentifierCodes.Should()
                .Equal(nasdaq.MainMarketIdentifierCode, nasdaq.FirstNorthIdentifierCode);
            market.DelayedTradeSource.Should().BeNull();
        }
    }

    [Fact]
    public void EuronextMarkets_AgreeWithTheEuronextDirectoryDefinitions()
    {
        var catalogued = EquityMarketCatalog
            .All.Where(market => market.DirectorySource == "euronext")
            .ToList();
        catalogued.Should().HaveCount(EuronextMarket.All.Count);
        foreach (var market in catalogued)
        {
            var euronext = EuronextMarket.FromSlug(market.Code["euronext-".Length..]);
            euronext.Should().NotBeNull(market.Code);
            euronext.MarketIdentifierCodes.Should().BeEquivalentTo(market.MarketIdentifierCodes);
            market.DelayedTradeSource.Should().Be("euronext");
            market.DelayedTradeLocationCode.Should().Be(euronext.TradesLocationCode);
        }
    }

    [Theory]
    [InlineData("XLIS", "euronext-lisbon", "PT", ".LS", "LIS", "Europe/Lisbon")]
    [InlineData("ALXP", "euronext-paris", "FR", ".PA", "PAR", "Europe/Paris")]
    [InlineData("XETR", "xetra", "DE", ".DE", "GER", "Europe/Berlin")]
    [InlineData("XLON", "lse", "GB", ".L", "LSE", "Europe/London")]
    [InlineData("FNSE", "nasdaq-stockholm", "SE", ".ST", "STO", "Europe/Stockholm")]
    [InlineData("XCSE", "nasdaq-copenhagen", "DK", ".CO", "CPH", "Europe/Copenhagen")]
    [InlineData("XMAD", "bme", "ES", ".MC", "MCE", "Europe/Madrid")]
    [InlineData("XWAR", "gpw", "PL", ".WA", "WSE", "Europe/Warsaw")]
    public void ByMarketIdentifierCode_ReturnsTheVerifiedProviderIdentity(
        string mic,
        string code,
        string country,
        string suffix,
        string exchange,
        string timeZone
    )
    {
        var market = EquityMarketCatalog.ByMarketIdentifierCode(mic);
        market.Code.Should().Be(code);
        market.CountryCode.Should().Be(country);
        market.YahooSuffix.Should().Be(suffix);
        market.YahooExchangeCode.Should().Be(exchange);
        market.TimeZoneId.Should().Be(timeZone);
        EquityMarketCatalog.TryGet(code).Should().BeSameAs(market);
    }

    [Fact]
    public void UnknownVenuesAndCodes_ResolveToNothing()
    {
        EquityMarketCatalog.ByMarketIdentifierCode("XNYS").Should().BeNull();
        EquityMarketCatalog.ByMarketIdentifierCode(null).Should().BeNull();
        EquityMarketCatalog.TryGet("nyse").Should().BeNull();
        EquityMarketCatalog.TryGet(null).Should().BeNull();
    }

    // Pinned to the first and last continuous prints and the closing-auction cluster of each market's
    // delayed-trades file for 2026-09-15, read in the market's own time zone.
    [Theory]
    [InlineData("euronext-paris", 9, 0, 17, 30, 17, 35)]
    [InlineData("euronext-amsterdam", 9, 0, 17, 30, 17, 35)]
    [InlineData("euronext-brussels", 9, 0, 17, 30, 17, 35)]
    [InlineData("euronext-milan", 9, 0, 17, 30, 17, 35)]
    [InlineData("euronext-dublin", 8, 0, 16, 28, 16, 30)]
    [InlineData("euronext-lisbon", 8, 0, 16, 30, 16, 35)]
    [InlineData("euronext-oslo", 9, 0, 16, 20, 16, 25)]
    public void EuronextSessions_MatchTheVenuesOwnPrints(
        string code,
        int openHour,
        int openMinute,
        int closeHour,
        int closeMinute,
        int auctionHour,
        int auctionMinute
    )
    {
        var market = EquityMarketCatalog.TryGet(code);
        market.SessionOpen.Should().Be(new TimeOnly(openHour, openMinute));
        market.SessionClose.Should().Be(new TimeOnly(closeHour, closeMinute));
        market.ClosingAuctionEnd.Should().Be(new TimeOnly(auctionHour, auctionMinute));
    }
}
