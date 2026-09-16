using System.Text.Json;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.EquityMarkets.HostedService.Services;
using Equibles.Integrations.Bme;
using Equibles.Integrations.Gpw;
using Equibles.Integrations.NasdaqNordic;
using Equibles.UnitTests.Euronext;

namespace Equibles.UnitTests.EquityMarkets;

// The Nasdaq Nordic, BME and GPW adapters over their trimmed real captures.
public class VenueDirectorySourceTests
{
    private const string Lei = "529900S9YM61OVI49P57";

    private static Task<string> Fixture(params string[] path) =>
        File.ReadAllTextAsync(Path.Combine([AppContext.BaseDirectory, "TestAssets", .. path]));

    private static Task<string> Nasdaq(string name) =>
        Fixture("EquityMarkets", "NasdaqNordic", name);

    private static Task<string> Bme(string name) => Fixture("EquityMarkets", "Bme", name);

    private static Task<string> Gpw(string name) => Fixture("EquityMarkets", "Gpw", name);

    private static FirdsInstrumentRecord Firds(string isin, string mic, string lei) =>
        new()
        {
            Authority = "ESMA",
            Isin = isin,
            Mic = mic,
            Lei = lei,
            Cfi = "ESVUFR",
            RelevantTradingVenue = mic,
        };

    [Fact]
    public async Task Nasdaq_CapturesBothListsUnderTheVenueEachNamesWithHyphenatedShareClasses()
    {
        var handler = new EuronextDirectoryTestHandler([
            await Nasdaq("screener-shares.STO.MAIN_MARKET.sample.json"),
            await Nasdaq("screener-shares.STO.FIRST_NORTH.sample.json"),
        ]);
        using var http = new HttpClient(handler);
        var source = new NasdaqNordicEquityMarketDirectorySource(new NasdaqNordicClient(http));
        var market = EquityMarketCatalog.TryGet("nasdaq-stockholm");
        source.SourceKey.Should().Be(market.DirectorySource);
        source.Supports(market).Should().BeTrue();
        source.Supports(EquityMarketCatalog.TryGet("nasdaq-helsinki")).Should().BeTrue();
        source.Supports(EquityMarketCatalog.TryGet("xetra")).Should().BeFalse();
        source.Supports(EquityMarketCatalog.TryGet("lse")).Should().BeFalse();

        var snapshot = await source.Capture(market, CancellationToken.None);

        snapshot.EvidenceSource.Should().Be("nasdaq-nordic-stockholm-screener-v1");
        snapshot
            .SourceUrl.AbsoluteUri.Should()
            .Be(
                "https://api.nasdaq.com/api/nordic/screener/shares?category=MAIN_MARKET&market=STO&tableonly=false"
            );
        handler.Requests.Select(request => request.Url.Query).Should().HaveCount(2);
        snapshot.Rows.Should().HaveCount(14);
        snapshot.Rows.Count(row => row.MarketIdentifierCode == "XSTO").Should().Be(10);
        snapshot.Rows.Count(row => row.MarketIdentifierCode == "FNSE").Should().Be(4);
        var volvo = snapshot.Rows.Single(row => row.Isin == "SE0000115446");
        volvo.Symbol.Should().Be("VOLV-B", "the venue writes the share class after a space");
        volvo.Name.Should().Be("Volvo B");
        volvo.ReportedCurrency.Should().Be("SEK");
        volvo.StatedPrimaryMarketIdentifierCode.Should().BeNull();
        volvo
            .SourceUrl.AbsoluteUri.Should()
            .Be("https://api.nasdaq.com/api/nordic/instruments/TX100/info?assetClass=SHARES");
        snapshot
            .Rows.Select(row => row.Symbol)
            .Should()
            .Contain(["ALIV-SDB", "BESQAB-PREF-B", "ATCO-A", "ATCO-B", "4C"]);
        snapshot.Rows.Single(row => row.Symbol == "VSURE").ReportedCurrency.Should().Be("EUR");
        using var payload = JsonDocument.Parse(snapshot.PayloadJson);
        var lists = payload.RootElement.GetProperty("Lists");
        lists.GetArrayLength().Should().Be(2);
        lists[1].GetProperty("MarketIdentifierCode").GetString().Should().Be("FNSE");
        lists[1].GetProperty("Rows").GetInt32().Should().Be(4);
        lists[0].GetProperty("Shares")[0].GetProperty("Symbol").GetString().Should().Be("AAK");
    }

    [Fact]
    public async Task Nasdaq_RefusesListsThatShareASymbolOrAnUnusableOne()
    {
        var main = await Nasdaq("screener-shares.STO.MAIN_MARKET.sample.json");
        var firstNorth = await Nasdaq("screener-shares.STO.FIRST_NORTH.sample.json");
        var market = EquityMarketCatalog.TryGet("nasdaq-stockholm");
        using var shared = new HttpClient(
            new EuronextDirectoryTestHandler([main, firstNorth.Replace("\"4C\"", "\"AAK\"")])
        );
        var collision = () =>
            new NasdaqNordicEquityMarketDirectorySource(new NasdaqNordicClient(shared)).Capture(
                market,
                CancellationToken.None
            );
        await collision.Should().ThrowAsync<InvalidDataException>().WithMessage("*one symbol*");
        using var unusable = new HttpClient(
            new EuronextDirectoryTestHandler([main.Replace("\"VOLV B\"", "\"VOLV/B\""), firstNorth])
        );
        var outside = () =>
            new NasdaqNordicEquityMarketDirectorySource(new NasdaqNordicClient(unusable)).Capture(
                market,
                CancellationToken.None
            );
        await outside.Should().ThrowAsync<InvalidDataException>().WithMessage("*listing contract*");
    }

    [Fact]
    public async Task Nasdaq_ConfirmsARowAgainstTheInstrumentOfItsOwnList()
    {
        var market = EquityMarketCatalog.TryGet("nasdaq-stockholm");
        var row = new EquityMarketDirectoryRow
        {
            Isin = "SE0000115446",
            MarketIdentifierCode = "XSTO",
            Symbol = "VOLV-B",
            Name = "Volvo B",
            ReportedCurrency = "SEK",
            SourceUrl = NasdaqNordicClient.InstrumentUrl("TX100"),
        };
        var firds = Firds("SE0000115446", "DSTO", Lei);
        using var http = new HttpClient(
            new EuronextDirectoryTestHandler([await Nasdaq("instrument-info.VOLV-B.json")])
        );
        var source = new NasdaqNordicEquityMarketDirectorySource(new NasdaqNordicClient(http));

        var product = await source.Resolve(market, row, firds, CancellationToken.None);

        product.SourceIssuerIdentifier.Should().Be(Lei);
        product.Name.Should().Be("Volvo B");
        product.SourceUrl.Should().Be(row.SourceUrl);
        product.ReportedCurrency.Should().Be("SEK");
        JsonSerializer
            .Serialize(product.Evidence)
            .Should()
            .Contain("\"Exchange\":\"Nasdaq Stockholm\"")
            .And.Contain("\"Symbol\":\"VOLV B\"");

        using var firstNorth = new HttpClient(
            new EuronextDirectoryTestHandler([await Nasdaq("instrument-info.VOLV-B.json")])
        );
        var otherList = () =>
            new NasdaqNordicEquityMarketDirectorySource(new NasdaqNordicClient(firstNorth)).Resolve(
                market,
                new EquityMarketDirectoryRow
                {
                    Isin = row.Isin,
                    MarketIdentifierCode = "FNSE",
                    Symbol = row.Symbol,
                    ReportedCurrency = row.ReportedCurrency,
                    SourceUrl = row.SourceUrl,
                },
                firds,
                CancellationToken.None
            );
        await otherList
            .Should()
            .ThrowAsync<InvalidDataException>(
                "a main-market instrument cannot confirm a First North row"
            );
        var withoutLei = () =>
            source.Resolve(market, row, Firds(row.Isin, "DSTO", null), CancellationToken.None);
        await withoutLei.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Bme_CapturesEveryCurrentLineOfEachListedCompanyFromItsOwnDetails()
    {
        var handler = new EuronextDirectoryTestHandler([
            await Bme("listed-companies.sample.json"),
            await Bme("share-details.ES0125220311.json"),
            await Bme("share-details.ES0167050915.json"),
            await Bme("share-details.ES0113900J37.json"),
            await Bme("share-details.NL0015001FS8.json"),
            await Bme("share-details.ES0171996087.json"),
            await Bme("share-details.ES0171996095.json"),
        ]);
        using var http = new HttpClient(handler);
        var source = new BmeEquityMarketDirectorySource(new BmeClient(http));
        var market = EquityMarketCatalog.TryGet("bme");
        source.SourceKey.Should().Be(market.DirectorySource);
        source.Supports(market).Should().BeTrue();
        source.Supports(EquityMarketCatalog.TryGet("gpw")).Should().BeFalse();

        var snapshot = await source.Capture(market, CancellationToken.None);

        snapshot.EvidenceSource.Should().Be("bme-continuous-market-listed-companies-v1");
        snapshot.SourceUrl.Should().Be(BmeClient.ListedCompaniesUrl);
        handler
            .Requests.Should()
            .HaveCount(7, "one list, five main lines and the one current second class");
        handler
            .Requests[6]
            .Url.Query.Should()
            .Be("?ISIN=ES0171996095", "the class B line is read after its class A names it");
        snapshot.Rows.Should().HaveCount(6);
        snapshot
            .Rows.Should()
            .AllSatisfy(row =>
            {
                row.MarketIdentifierCode.Should().Be("XMAD");
                row.ReportedCurrency.Should().Be("EUR");
                row.StatedPrimaryMarketIdentifierCode.Should().BeNull();
                row.SourceUrl.Should().Be(BmeClient.ShareDetailsUrl(row.Isin));
            });
        snapshot.Rows.Single(row => row.Isin == "ES0125220311").Symbol.Should().Be("ANA");
        var classB = snapshot.Rows.Single(row => row.Isin == "ES0171996095");
        classB.Symbol.Should().Be("GRF-P", "the venue writes the share class after a dot");
        classB.Name.Should().Be("GRIFOLS CLASE B");
        snapshot
            .Rows.Should()
            .Contain(
                row => row.Isin == "NL0015001FS8",
                "the gate, not the adapter, decides the home"
            );
        using var payload = JsonDocument.Parse(snapshot.PayloadJson);
        payload.RootElement.GetProperty("TotalResults").GetInt32().Should().Be(5);
        payload.RootElement.GetProperty("Shares").GetArrayLength().Should().Be(6);
    }

    [Fact]
    public async Task Bme_ConfirmsARowAgainstItsDetailsAndNamesTheIssuerByItsCode()
    {
        var market = EquityMarketCatalog.TryGet("bme");
        var row = new EquityMarketDirectoryRow
        {
            Isin = "ES0171996095",
            MarketIdentifierCode = "XMAD",
            Symbol = "GRF-P",
            Name = "GRIFOLS CLASE B",
            ReportedCurrency = "EUR",
            SourceUrl = BmeClient.ShareDetailsUrl("ES0171996095"),
        };
        using var http = new HttpClient(
            new EuronextDirectoryTestHandler([await Bme("share-details.ES0171996095.json")])
        );
        var source = new BmeEquityMarketDirectorySource(new BmeClient(http));

        var product = await source.Resolve(
            market,
            row,
            Firds(row.Isin, "XMAD", Lei),
            CancellationToken.None
        );

        product.SourceIssuerIdentifier.Should().Be("71996");
        product.Name.Should().Be("GRIFOLS CLASE B");
        product.SourceUrl.Should().Be(row.SourceUrl);
        product.ReportedCurrency.Should().Be("EUR");
        JsonSerializer.Serialize(product.Evidence).Should().Contain("\"Ticker\":\"GRF.P\"");

        using var other = new HttpClient(
            new EuronextDirectoryTestHandler([await Bme("share-details.ES0171996095.json")])
        );
        var mismatch = () =>
            new BmeEquityMarketDirectorySource(new BmeClient(other)).Resolve(
                market,
                new EquityMarketDirectoryRow
                {
                    Isin = row.Isin,
                    MarketIdentifierCode = "XMAD",
                    Symbol = "GRF",
                    ReportedCurrency = "EUR",
                    SourceUrl = row.SourceUrl,
                },
                Firds(row.Isin, "XMAD", Lei),
                CancellationToken.None
            );
        await mismatch.Should().ThrowAsync<InvalidDataException>().WithMessage("*conflict*");
    }

    [Fact]
    public async Task Gpw_CapturesTheUnionOfTheThreeTablesUnderTheCompanyPageAddress()
    {
        var handler = new EuronextDirectoryTestHandler([
            await Gpw("quotations.continuous.sample.html"),
            await Gpw("quotations.fix1.html"),
            await Gpw("quotations.fix2.sample.html"),
        ]);
        using var http = new HttpClient(handler);
        var source = new GpwEquityMarketDirectorySource(new GpwClient(http));
        var market = EquityMarketCatalog.TryGet("gpw");
        source.SourceKey.Should().Be(market.DirectorySource);
        source.Supports(market).Should().BeTrue();
        source.Supports(EquityMarketCatalog.TryGet("bme")).Should().BeFalse();

        var snapshot = await source.Capture(market, CancellationToken.None);

        snapshot.EvidenceSource.Should().Be("gpw-main-market-quotations-v1");
        snapshot.SourceUrl.Should().Be(GpwClient.TableUrl("continuous"));
        snapshot.Rows.Should().HaveCount(12);
        snapshot
            .Rows.Should()
            .AllSatisfy(row =>
            {
                row.MarketIdentifierCode.Should().Be("XWAR");
                row.ReportedCurrency.Should().Be("PLN");
                row.StatedPrimaryMarketIdentifierCode.Should().BeNull();
                row.SourceUrl.Should().Be(GpwClient.FactsheetUrl(row.Isin));
            });
        var studio = snapshot.Rows.Single(row => row.Isin == "PL11BTS00015");
        studio.Symbol.Should().Be("11B");
        studio.Name.Should().Be("11BIT");
        snapshot.Rows.Single(row => row.Isin == "PLPBG0000029").Name.Should().Be("PBG /Z /6,/7");
        snapshot.Rows.Should().Contain(row => row.Isin == "SE0001856519");
        using var payload = JsonDocument.Parse(snapshot.PayloadJson);
        var tables = payload.RootElement.GetProperty("Tables");
        tables.GetArrayLength().Should().Be(3);
        tables[2].GetProperty("TradingSystem").GetString().Should().Be("fix2");
        tables[2].GetProperty("Rows").GetInt32().Should().Be(3);

        using var repeated = new HttpClient(
            new EuronextDirectoryTestHandler([
                await Gpw("quotations.continuous.sample.html"),
                await Gpw("quotations.continuous.sample.html"),
                await Gpw("quotations.fix2.sample.html"),
            ])
        );
        var twice = () =>
            new GpwEquityMarketDirectorySource(new GpwClient(repeated)).Capture(
                market,
                CancellationToken.None
            );
        await twice.Should().ThrowAsync<InvalidDataException>().WithMessage("*repeat*");
    }

    [Fact]
    public async Task Gpw_ConfirmsARowAgainstTheCompanyPageAndTheFirdsLei()
    {
        var market = EquityMarketCatalog.TryGet("gpw");
        var row = new EquityMarketDirectoryRow
        {
            Isin = "PL11BTS00015",
            MarketIdentifierCode = "XWAR",
            Symbol = "11B",
            Name = "11BIT",
            ReportedCurrency = "PLN",
            SourceUrl = GpwClient.FactsheetUrl("PL11BTS00015"),
        };
        using var http = new HttpClient(
            new EuronextDirectoryTestHandler([
                await Gpw("company-factsheet.PL11BTS00015.excerpt.html"),
            ])
        );
        var source = new GpwEquityMarketDirectorySource(new GpwClient(http));

        var product = await source.Resolve(
            market,
            row,
            Firds(row.Isin, "XWAR", Lei),
            CancellationToken.None
        );

        product.SourceIssuerIdentifier.Should().Be(Lei);
        product.Name.Should().Be("11BIT");
        product.SourceUrl.Should().Be(row.SourceUrl);
        product.ReportedCurrency.Should().Be("PLN");
        JsonSerializer.Serialize(product.Evidence).Should().Contain("\"Shortcut\":\"11B\"");

        using var other = new HttpClient(
            new EuronextDirectoryTestHandler([
                await Gpw("company-factsheet.PL11BTS00015.excerpt.html"),
            ])
        );
        var mismatch = () =>
            new GpwEquityMarketDirectorySource(new GpwClient(other)).Resolve(
                market,
                new EquityMarketDirectoryRow
                {
                    Isin = row.Isin,
                    MarketIdentifierCode = "XWAR",
                    Symbol = "11BIT",
                    ReportedCurrency = "PLN",
                    SourceUrl = row.SourceUrl,
                },
                Firds(row.Isin, "XWAR", Lei),
                CancellationToken.None
            );
        await mismatch.Should().ThrowAsync<InvalidDataException>().WithMessage("*conflict*");
        var withoutLei = () =>
            source.Resolve(market, row, Firds(row.Isin, "XWAR", null), CancellationToken.None);
        await withoutLei.Should().ThrowAsync<InvalidDataException>();
    }
}
