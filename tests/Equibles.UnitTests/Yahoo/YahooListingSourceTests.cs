using Equibles.CommonStocks.Data.Models;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooListingSourceTests
{
    private static PriceSeriesTarget Target(
        string ticker,
        string country,
        string mic,
        string isin,
        string currency = "EUR",
        decimal? multiplier = 1m
    ) =>
        new(
            ticker,
            Guid.NewGuid(),
            Guid.NewGuid(),
            false,
            MarketCountryCode: country,
            MarketIdentifierCode: mic,
            Isin: isin,
            TradingCurrency: currency,
            QuoteUnitMultiplier: multiplier
        );

    [Fact]
    public void LondonQuotesInPenceAndAFewOfItsLinesInAnotherCurrency()
    {
        var pence = Target("BT-A", "GB", "XLON", "GB0030913577", "GBP", 0.01m);
        pence.ProviderSymbol.Should().Be("BT-A.L");
        var identity = new YahooChartSourceIdentity
        {
            Symbol = "BT-A.L",
            Currency = "GBp",
            ExchangeCode = "LSE",
            InstrumentType = "EQUITY",
            ExchangeTimeZone = "Europe/London",
        };
        YahooListingSource.MatchesChart(pence, identity).Should().BeTrue();
        identity.Currency = "GBP";
        YahooListingSource
            .MatchesChart(pence, identity)
            .Should()
            .BeFalse("a pound quote is a different line of the same security");

        var dollars = Target("AOF", "GB", "XLON", "KYG012921535", "USD", 1m);
        YahooListingSource
            .MatchesChart(
                dollars,
                new YahooChartSourceIdentity
                {
                    Symbol = "AOF.L",
                    Currency = "USD",
                    ExchangeCode = "LSE",
                    InstrumentType = "EQUITY",
                    ExchangeTimeZone = "Europe/London",
                }
            )
            .Should()
            .BeTrue();

        var growth = Target("4BB", "GB", "AIMX", "GB00BMCLYF79", "GBP", 0.01m);
        YahooListingSource
            .MatchesChart(
                growth,
                new YahooChartSourceIdentity
                {
                    Symbol = "4BB.L",
                    Currency = "GBp",
                    ExchangeCode = "LSE",
                    InstrumentType = "EQUITY",
                    ExchangeTimeZone = "Europe/London",
                }
            )
            .Should()
            .BeTrue("the growth market shares the exchange's provider identity");
    }

    [Theory]
    [InlineData("XLIS", "PT", "ALTR.LS", "LIS", "Europe/Lisbon")]
    [InlineData("ENXL", "PT", "ALTR.LS", "LIS", "Europe/Lisbon")]
    [InlineData("ALXL", "PT", "ALTR.LS", "LIS", "Europe/Lisbon")]
    [InlineData("XPAR", "FR", "ALTR.PA", "PAR", "Europe/Paris")]
    [InlineData("XETR", "DE", "ALTR.DE", "GER", "Europe/Berlin")]
    [InlineData("MTAA", "IT", "ALTR.MI", "MIL", "Europe/Rome")]
    public void CatalogCandidateRequiresTheMarketsChartIdentity(
        string mic,
        string country,
        string symbol,
        string exchange,
        string timeZone
    )
    {
        var target = Target("ALTR", country, mic, "PTALT0AE0002");
        target.ProviderSymbol.Should().Be(symbol);
        var identity = new YahooChartSourceIdentity
        {
            Symbol = symbol,
            Currency = "EUR",
            ExchangeCode = exchange,
            InstrumentType = "EQUITY",
            ExchangeTimeZone = timeZone,
        };
        YahooListingSource.MatchesChart(target, identity).Should().BeTrue();
        YahooListingSource.MatchesChart(target, null).Should().BeFalse();
        identity.ExchangeCode = "NMS";
        YahooListingSource.MatchesChart(target, identity).Should().BeFalse();
        YahooListingSource
            .EvidenceSource(YahooListingSource.Market(target))
            .Should()
            .Be("yahoo-" + EquityMarketCatalog.ByMarketIdentifierCode(mic).Code + "-chart-v1");
    }

    [Theory]
    [InlineData("AIR", "US", null, "AIR")]
    [InlineData("AIR", "FR", "XPAR", "AIR.PA")]
    [InlineData("SAP", "DE", "XETR", "SAP.DE")]
    [InlineData("SHEL", "GB", "XLON", "SHEL.L")]
    [InlineData("VOLV-B", "SE", "XSTO", "VOLV-B.ST")]
    [InlineData("4C", "SE", "FNSE", "4C.ST")]
    [InlineData("MAERSK-B", "DK", "XCSE", "MAERSK-B.CO")]
    [InlineData("NOKIA", "FI", "XHEL", "NOKIA.HE")]
    [InlineData("GRF-P", "ES", "XMAD", "GRF-P.MC")]
    [InlineData("11B", "PL", "XWAR", "11B.WA")]
    [InlineData("4BB", "GB", "AIMX", "4BB.L")]
    [InlineData("BT-A", "GB", "XLON", "BT-A.L")]
    [InlineData("600000", "CN", "XSHG", "600000.SS")]
    [InlineData("000001", "CN", "XSHE", "000001.SZ")]
    [InlineData("00001", "HK", "XHKG", "0001.HK")]
    [InlineData("80700", "HK", "XHKG", "80700.HK")]
    [InlineData("0001", "HK", "XHKG", null)]
    [InlineData("1", "HK", "XHKG", null)]
    [InlineData("ALPHA", "HK", "XHKG", null)]
    [InlineData("00000", "HK", "XHKG", null)]
    [InlineData("920000", "CN", "BJSE", null)]
    [InlineData("NESN", "CH", "XSWX", null)]
    [InlineData("AIR", "FR", "XETR", null)]
    [InlineData("AIR", "FR", null, null)]
    [InlineData(" ", "US", null, null)]
    public void VenueSymbol_QualifiesByCatalogSuffix_OrRefusesAnUnknownVenue(
        string ticker,
        string country,
        string mic,
        string expected
    )
    {
        YahooListingSource.ProviderSymbol(ticker, country, mic).Should().Be(expected);
    }

    [Fact]
    public void PenceQuotedChart_MatchesOnlyAListingStoredAsPoundsWithTheHundredthMultiplier()
    {
        var pence = Target("SHEL", "GB", "XLON", "GB00BP6MXD84", "GBP", 0.01m);
        pence.ProviderSymbol.Should().Be("SHEL.L");
        var identity = new YahooChartSourceIdentity
        {
            Symbol = "SHEL.L",
            Currency = "GBp",
            ExchangeCode = "LSE",
            InstrumentType = "EQUITY",
            ExchangeTimeZone = "Europe/London",
        };
        YahooListingSource.MatchesChart(pence, identity).Should().BeTrue();
        YahooListingSource
            .MatchesChart(Target("SHEL", "GB", "XLON", "GB00BP6MXD84", "GBP", 1m), identity)
            .Should()
            .BeFalse("a pound-quoted listing cannot take pence bars");
        identity.Currency = "GBP";
        YahooListingSource.MatchesChart(pence, identity).Should().BeFalse();
    }

    [Fact]
    public void ChartInAnotherCurrencyOrWithoutAStatedUnit_NeverMatches()
    {
        var target = Target("ALTR", "PT", "XLIS", "PTALT0AE0002");
        var identity = new YahooChartSourceIdentity
        {
            Symbol = "ALTR.LS",
            Currency = "USD",
            ExchangeCode = "LIS",
            InstrumentType = "EQUITY",
            ExchangeTimeZone = "Europe/Lisbon",
        };
        YahooListingSource.MatchesChart(target, identity).Should().BeFalse();
        identity.Currency = "EUR";
        YahooListingSource
            .MatchesChart(Target("ALTR", "PT", "XLIS", "PTALT0AE0002", null, null), identity)
            .Should()
            .BeFalse("an unverified listing states no unit the chart could agree with");
    }

    [Theory]
    [InlineData("PT", "XLON", "PTALT0AE0002")]
    [InlineData("GB", "XLIS", "PTALT0AE0002")]
    [InlineData(null, "XLIS", "PTALT0AE0002")]
    [InlineData("PT", "XLIS", null)]
    [InlineData("CH", "XSWX", "CH0012032048")]
    public void UnsupportedIdentityCannotBecomeAProviderSymbol(
        string country,
        string mic,
        string isin
    )
    {
        var target = Target("SAME", country, mic, isin);
        target.ProviderSymbol.Should().BeNull();
        YahooListingSource.Market(target).Should().BeNull();
        var binding = () => YahooListingSource.SourceBinding(target);
        binding.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UsTargets_KeepTheBareTickerAndNoCatalogMarket()
    {
        var target = new PriceSeriesTarget("AAPL", Guid.NewGuid(), Guid.NewGuid(), true);
        target.ProviderSymbol.Should().Be("AAPL");
        YahooListingSource.Market(target).Should().BeNull();
    }

    [Fact]
    public void MatchesListing_RequiresTheVerifiedListingToStateTheTargetsUnit()
    {
        var target = Target("ALTR", "PT", "XLIS", "PTALT0AE0002");
        var listing = new EquityListing
        {
            Id = target.EquityListingId,
            Security = new EquitySecurity
            {
                EquityIssuerId = target.EquityIssuerId,
                Isin = "PTALT0AE0002",
            },
            Ticker = "ALTR",
            MarketIdentifierCode = "XLIS",
            MarketCountryCode = "PT",
            TradingCurrency = "EUR",
            QuoteUnitMultiplier = 1m,
            IdentityState = EquityIdentityState.Verified,
        };
        YahooListingSource.MatchesListing(target, listing).Should().BeTrue();
        listing.QuoteUnitMultiplier = 0.01m;
        YahooListingSource.MatchesListing(target, listing).Should().BeFalse();
        listing.QuoteUnitMultiplier = 1m;
        listing.TradingCurrency = null;
        YahooListingSource.MatchesListing(target, listing).Should().BeFalse();
        listing.TradingCurrency = "EUR";
        listing.IdentityState = EquityIdentityState.Legacy;
        YahooListingSource.MatchesListing(target, listing).Should().BeFalse();
    }

    [Theory]
    [InlineData("600000", "CN", "XSHG", "CNY", "600000.SS", "SHH", "Asia/Shanghai")]
    [InlineData("000001", "CN", "XSHE", "CNY", "000001.SZ", "SHZ", "Asia/Shanghai")]
    [InlineData("00001", "HK", "XHKG", "HKD", "0001.HK", "HKG", "Asia/Hong_Kong")]
    public void SourceIdentifiedInstrumentWithoutIsinStillRequiresExactChartAndListingIdentity(
        string ticker,
        string country,
        string mic,
        string currency,
        string symbol,
        string exchange,
        string zone
    )
    {
        var securityId = Guid.NewGuid();
        var target = Target(ticker, country, mic, null, currency) with
        {
            EquitySecurityId = securityId,
        };
        var chart = new YahooChartSourceIdentity
        {
            Symbol = symbol,
            Currency = currency,
            ExchangeCode = exchange,
            InstrumentType = "EQUITY",
            ExchangeTimeZone = zone,
        };
        target.ProviderSymbol.Should().Be(symbol);
        YahooListingSource.MatchesChart(target, chart).Should().BeTrue();
        YahooListingSource.SourceBinding(target).EquitySecurityId.Should().Be(securityId);
        var listing = new EquityListing
        {
            Id = target.EquityListingId,
            EquitySecurityId = securityId,
            Security = new EquitySecurity
            {
                Id = securityId,
                EquityIssuerId = target.EquityIssuerId,
            },
            Ticker = ticker,
            MarketCountryCode = country,
            MarketIdentifierCode = mic,
            TradingCurrency = currency,
            QuoteUnitMultiplier = 1m,
            IdentityState = EquityIdentityState.Verified,
        };
        YahooListingSource.MatchesListing(target, listing).Should().BeTrue();
        listing.EquitySecurityId = Guid.NewGuid();
        YahooListingSource.MatchesListing(target, listing).Should().BeFalse();
        chart.Currency = "USD";
        YahooListingSource.MatchesChart(target, chart).Should().BeFalse();
        (target with { EquitySecurityId = null }).ProviderSymbol.Should().BeNull();
    }

    [Fact]
    public void SourceBinding_RefusesATargetWithoutAVerifiedQuotationUnit()
    {
        var binding = () =>
            YahooListingSource.SourceBinding(
                Target("TTE", "FR", "XPAR", "FR0000120271", "EUR", null)
            );
        binding.Should().Throw<InvalidOperationException>().WithMessage("*quotation unit*");
    }

    [Fact]
    public void SourceBinding_CoversEveryVenueOfTheMarket()
    {
        var target = Target("TTE", "FR", "XPAR", "FR0000120271");
        var binding = YahooListingSource.SourceBinding(target);
        binding.Currency.Should().Be("EUR");
        binding.QuoteUnitMultiplier.Should().Be(1m);
        binding
            .SourceMarketIdentifierCodes.Should()
            .BeEquivalentTo(["XPAR", "ALXP", "XMLI", "XPMC"]);
        var evidence = YahooListingSource.QuotationEvidence(
            target,
            new YahooChartSourceIdentity { Symbol = "TTE.PA", Currency = "EUR" }
        );
        evidence.Source.Should().Be("yahoo-euronext-paris-chart-v1");
        evidence
            .PayloadJson.Should()
            .Contain("\"RequestedSymbol\":\"TTE.PA\"")
            .And.Contain("TTE.PA");
    }
}
