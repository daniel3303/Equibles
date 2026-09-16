using Equibles.Integrations.NasdaqNordic;
using Equibles.UnitTests.Euronext;

namespace Equibles.UnitTests.EquityMarkets;

public class NasdaqNordicTests
{
    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "EquityMarkets",
                "NasdaqNordic",
                name
            )
        );

    [Fact]
    public void Markets_NameTheListAndTheExchangeLabelOfEachVenue()
    {
        NasdaqNordicMarket.All.Select(market => market.Slug).Should().OnlyHaveUniqueItems();
        NasdaqNordicMarket.FromSlug("stockholm").Should().BeSameAs(NasdaqNordicMarket.Stockholm);
        NasdaqNordicMarket.FromSlug("oslo").Should().BeNull();
        var helsinki = NasdaqNordicMarket.Helsinki;
        helsinki.MarketIdentifierCode(NasdaqNordicCategory.MainMarket).Should().Be("XHEL");
        helsinki.MarketIdentifierCode(NasdaqNordicCategory.FirstNorth).Should().Be("FNFI");
        helsinki.ExchangeLabel(NasdaqNordicCategory.MainMarket).Should().Be("Nasdaq Helsinki");
        helsinki
            .ExchangeLabel(NasdaqNordicCategory.FirstNorth)
            .Should()
            .Be("First North GM Finland");
        helsinki.CategoryOf("FNFI").Should().Be(NasdaqNordicCategory.FirstNorth);
        foreach (var market in NasdaqNordicMarket.All)
        foreach (var category in NasdaqNordicMarket.Categories)
            market
                .ExchangeLabels(category)
                .Should()
                .Equal(market.ExchangeLabel(category), market.ExchangeLabel(category) + " Auction");
        NasdaqNordicMarket
            .All.SelectMany(market =>
                NasdaqNordicMarket.Categories.SelectMany(market.ExchangeLabels)
            )
            .Should()
            .OnlyHaveUniqueItems("no label may confirm a row of another list or another market");
        helsinki.CategoryOf("XSTO").Should().BeNull();
        NasdaqNordicClient
            .ShareListUrl(NasdaqNordicMarket.Copenhagen, NasdaqNordicCategory.FirstNorth)
            .AbsoluteUri.Should()
            .Be(
                "https://api.nasdaq.com/api/nordic/screener/shares?category=FIRST_NORTH&market=CPH&tableonly=false"
            );
        var instrument = NasdaqNordicClient.InstrumentUrl("TX100");
        instrument
            .AbsoluteUri.Should()
            .Be("https://api.nasdaq.com/api/nordic/instruments/TX100/info?assetClass=SHARES");
        NasdaqNordicClient.IsInstrumentUrl(instrument).Should().BeTrue();
        NasdaqNordicClient
            .IsInstrumentUrl(new Uri("https://api.nasdaq.com/api/nordic/instruments/TX100/info"))
            .Should()
            .BeFalse();
        NasdaqNordicClient
            .IsInstrumentUrl(
                new Uri("https://example.com/api/nordic/instruments/TX100/info?assetClass=SHARES")
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task ShareList_ReadsEveryRowWithTheVenuesOwnSymbolSpelling()
    {
        var list = NasdaqNordicParser.ReadShareList(
            await Fixture("screener-shares.STO.MAIN_MARKET.sample.json")
        );
        list.Shares.Should().HaveCount(10);
        var volvo = list.Shares.Single(share => share.Isin == "SE0000115446");
        volvo.Symbol.Should().Be("VOLV B", "the parser keeps the venue's spelling");
        volvo.FullName.Should().Be("Volvo B");
        volvo.Currency.Should().Be("SEK");
        volvo.OrderbookId.Should().Be("TX100");
        volvo.AssetClass.Should().Be("SHARES");
        list.Shares.Single(share => share.Symbol == "VSURE").Currency.Should().Be("EUR");
        list.Shares.Select(share => share.Symbol)
            .Should()
            .Contain(["ALIV SDB", "BESQAB PREF B", "ATCO A", "ATCO B"]);
        var firstNorth = NasdaqNordicParser.ReadShareList(
            await Fixture("screener-shares.STO.FIRST_NORTH.sample.json")
        );
        firstNorth.Shares.Should().HaveCount(4);
        firstNorth.Shares[0].Symbol.Should().Be("4C");
        firstNorth.Shares[0].OrderbookId.Should().Be("TX4391034");
    }

    [Theory]
    [InlineData("status", "\"rCode\": 200", "\"rCode\": 500")]
    [InlineData("partial", "\"total\": 10", "\"total\": 411")]
    [InlineData("paged", "\"totalPages\": 1", "\"totalPages\": 2")]
    [InlineData("total-text", "\"total\": 10", "\"total\": \"10\"")]
    [InlineData("no-pagination", "\"pagination\"", "\"paging\"")]
    [InlineData("asset-class", "\"assetClass\": \"SHARES\"", "\"assetClass\": \"ETF\"")]
    [InlineData("bad-isin", "\"isin\": \"SE0000115446\"", "\"isin\": \"SE0000115447\"")]
    [InlineData("no-symbol", "\"symbol\": \"VOLV B\"", "\"symbol\": \"\"")]
    [InlineData("duplicate-symbol", "\"symbol\": \"ATCO A\"", "\"symbol\": \"ATCO B\"")]
    [InlineData("duplicate-isin", "\"isin\": \"SE0011337708\"", "\"isin\": \"SE0000115446\"")]
    [InlineData("bad-currency", "\"currency\": \"EUR\"", "\"currency\": \"euro\"")]
    [InlineData("no-orderbook", "\"orderbookId\": \"TX100\"", "\"orderbookId\": \"\"")]
    public async Task ChangedShapeOrInvalidShareIdentity_RefusesTheWholeList(
        string scenario,
        string original,
        string replacement
    )
    {
        var json = await Fixture("screener-shares.STO.MAIN_MARKET.sample.json");
        json.Should().Contain(original, scenario);
        var read = () => NasdaqNordicParser.ReadShareList(json.Replace(original, replacement));
        read.Should().Throw<InvalidDataException>(scenario);
        var notJson = () => NasdaqNordicParser.ReadShareList("<html>");
        notJson.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Instrument_ReadsTheHeaderThatNamesTheLinesExchange()
    {
        var volvo = NasdaqNordicParser.ReadInstrument(await Fixture("instrument-info.VOLV-B.json"));
        volvo.Symbol.Should().Be("VOLV B");
        volvo.CompanyName.Should().Be("Volvo B");
        volvo.Exchange.Should().Be("Nasdaq Stockholm");
        volvo.Segment.Should().Be("Large Cap");
        volvo.Isin.Should().Be("SE0000115446");
        volvo.Currency.Should().Be("SEK");
        NasdaqNordicParser
            .ReadInstrument(await Fixture("instrument-info.4C.json"))
            .Exchange.Should()
            .Be("First North GM Sweden");
        NasdaqNordicParser
            .ReadInstrument(await Fixture("instrument-info.MAERSK-A.json"))
            .Exchange.Should()
            .Be("Nasdaq Copenhagen");
        NasdaqNordicParser
            .ReadInstrument(await Fixture("instrument-info.AALLON.json"))
            .Exchange.Should()
            .Be("First North GM Finland");
        var missing = () =>
            NasdaqNordicParser.ReadInstrument(
                "{\"data\":{\"qdHeader\":{\"symbol\":\"X\"}},\"status\":{\"rCode\":200}}"
            );
        missing.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Client_FetchesTheListAndTheInstrumentFromTheVenueOriginOnly()
    {
        var handler = new EuronextDirectoryTestHandler([
            await Fixture("screener-shares.STO.FIRST_NORTH.sample.json"),
            await Fixture("instrument-info.4C.json"),
        ]);
        using var http = new HttpClient(handler);
        var client = new NasdaqNordicClient(http);
        var list = await client.GetShares(
            NasdaqNordicMarket.Stockholm,
            NasdaqNordicCategory.FirstNorth
        );
        list.MarketCode.Should().Be("STO");
        list.Category.Should().Be(NasdaqNordicCategory.FirstNorth);
        list.SourceUrl.Query.Should().Contain("category=FIRST_NORTH").And.Contain("market=STO");
        var instrument = await client.GetInstrument(NasdaqNordicClient.InstrumentUrl("TX4391034"));
        instrument.Isin.Should().Be("SE0017936891");
        instrument.SourceUrl.AbsolutePath.Should().Be("/api/nordic/instruments/TX4391034/info");
        handler.Requests.Should().HaveCount(2);
        handler
            .Requests.Should()
            .AllSatisfy(request =>
            {
                request.Method.Should().Be(HttpMethod.Get);
                request.Url.Host.Should().Be("api.nasdaq.com");
            });
        var elsewhere = () =>
            client.GetInstrument(new Uri("https://example.com/api/nordic/instruments/TX1/info"));
        await elsewhere.Should().ThrowAsync<InvalidDataException>();
    }
}
