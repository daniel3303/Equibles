using Equibles.EquityMarkets.Data.Catalog;
using Equibles.Integrations.Euronext;

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
            market.MarketIdentifierCodes.Should().AllSatisfy(mic => mic.Should().MatchRegex("^[A-Z0-9]{4}$"));
            market.YahooSuffix.Should().StartWith(".");
            market.YahooExchangeCode.Should().NotBeNullOrWhiteSpace();
            TimeZoneInfo.FindSystemTimeZoneById(market.TimeZoneId).Should().NotBeNull();
            market.SessionOpen.Should().BeBefore(market.SessionClose);
            market.SessionClose.Should().BeOnOrBefore(market.ClosingAuctionEnd);
            EquityQuotationUnits.TryResolve(market.Currency, out _, out _).Should().BeTrue();
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

    [Fact]
    public void Lisbon_KeepsItsEarlierSessionAndTheOneTimeSeed()
    {
        var lisbon = EquityMarketCatalog.TryGet(EquityMarketRegistrationSeed.LisbonCode);
        lisbon.SessionClose.Should().Be(new TimeOnly(16, 30));
        lisbon.ClosingAuctionEnd.Should().Be(new TimeOnly(16, 35));
        EquityMarketRegistrationSeed.InitiallyEnabled(true).Should().Equal("euronext-lisbon");
        EquityMarketRegistrationSeed.InitiallyEnabled(false).Should().BeEmpty();
    }
}
