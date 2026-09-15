using System.Text.Json;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.EquityMarkets.HostedService.Services;
using Equibles.Integrations.Euronext;
using Equibles.Integrations.Xetra;
using Equibles.UnitTests.Euronext;

namespace Equibles.UnitTests.EquityMarkets;

public class EquityMarketDirectorySourceTests
{
    private static Task<string> Fixture(params string[] path) =>
        File.ReadAllTextAsync(Path.Combine([AppContext.BaseDirectory, "TestAssets", .. path]));

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
    public async Task Xetra_CapturesOnlyTradableSharesUnderAStableRowAddress()
    {
        var page = await Fixture("EquityMarkets", "Xetra", "tradable-instruments-page.html");
        var csv = await Fixture(
            "EquityMarkets",
            "Xetra",
            "t7-xetr-allTradableInstruments.sample.csv"
        );
        using var http = new HttpClient(new EuronextDirectoryTestHandler([page, csv]));
        var source = new XetraEquityMarketDirectorySource(new XetraInstrumentListClient(http));
        var market = EquityMarketCatalog.TryGet("xetra");
        source.SourceKey.Should().Be(market.DirectorySource);
        source.Supports(market).Should().BeTrue();
        source.Supports(EquityMarketCatalog.TryGet("euronext-paris")).Should().BeFalse();

        var snapshot = await source.Capture(market, CancellationToken.None);

        snapshot.EvidenceSource.Should().Be("xetra-all-tradable-instruments-v1");
        snapshot
            .SourceUrl.AbsoluteUri.Should()
            .Be(
                "https://www.cashmarket.deutsche-boerse.com/cash-en/trading/Tradable-Instruments-Xetra"
            );
        snapshot
            .Rows.Should()
            .HaveCount(30, "the share pending deletion and the funds are not directory listings");
        snapshot
            .Rows.Should()
            .AllSatisfy(row =>
            {
                row.MarketIdentifierCode.Should().Be("XETR");
                row.ReportedCurrency.Should().Be("EUR");
                row.SourceUrl.GetLeftPart(UriPartial.Path)
                    .Should()
                    .Be(snapshot.SourceUrl.AbsoluteUri);
                row.SourceUrl.Fragment.Should().Be($"#{row.Isin}-XETR");
            });
        var strabag = snapshot.Rows.Single(row => row.Isin == "AT000000STR1");
        strabag.Symbol.Should().Be("XD4");
        strabag.Name.Should().Be("STRABAG SE");
        strabag
            .StatedPrimaryMarketIdentifierCode.Should()
            .Be("XWBO", "the list states each share's primary market");
        snapshot
            .Rows.Single(row => row.Isin == "AT0000785407")
            .StatedPrimaryMarketIdentifierCode.Should()
            .Be("XFRA");
        using var payload = JsonDocument.Parse(snapshot.PayloadJson);
        payload
            .RootElement.GetProperty("SourceUrl")
            .GetString()
            .Should()
            .Contain("/data/t7-xetr-allTradableInstruments.csv");
        payload.RootElement.GetProperty("Rows").GetInt32().Should().Be(36);
        payload.RootElement.GetProperty("Shares").GetArrayLength().Should().Be(30);
    }

    [Fact]
    public async Task Xetra_RefusesAFileThatGivesOneSymbolToTwoShares()
    {
        var page = await Fixture("EquityMarkets", "Xetra", "tradable-instruments-page.html");
        var csv = await Fixture(
            "EquityMarkets",
            "Xetra",
            "t7-xetr-allTradableInstruments.sample.csv"
        );
        using var http = new HttpClient(
            new EuronextDirectoryTestHandler([page, csv.Replace(";RAW;", ";XD4;")])
        );
        var source = new XetraEquityMarketDirectorySource(new XetraInstrumentListClient(http));
        var capture = () =>
            source.Capture(EquityMarketCatalog.TryGet("xetra"), CancellationToken.None);
        await capture.Should().ThrowAsync<InvalidDataException>().WithMessage("*one symbol*");
    }

    [Fact]
    public async Task Xetra_ResolvesTheIssuerOnlyThroughTheFirdsLei()
    {
        var source = new XetraEquityMarketDirectorySource(
            new XetraInstrumentListClient(new HttpClient())
        );
        var market = EquityMarketCatalog.TryGet("xetra");
        var row = new EquityMarketDirectoryRow
        {
            Isin = "AT000000STR1",
            MarketIdentifierCode = "XETR",
            Symbol = "XD4",
            Name = "STRABAG SE",
            ReportedCurrency = "EUR",
            StatedPrimaryMarketIdentifierCode = "XWBO",
            SourceUrl = new Uri(
                "https://www.cashmarket.deutsche-boerse.com/cash-en/trading/Tradable-Instruments-Xetra#AT000000STR1-XETR"
            ),
        };
        var product = await source.Resolve(
            market,
            row,
            Firds("AT000000STR1", "XETB", "529900S9YM61OVI49P57"),
            CancellationToken.None
        );
        product.SourceIssuerIdentifier.Should().Be("529900S9YM61OVI49P57");
        product.SourceUrl.Should().Be(row.SourceUrl);
        product.ReportedCurrency.Should().Be("EUR");
        JsonSerializer
            .Serialize(product.Evidence)
            .Should()
            .Contain("\"StatedPrimaryMarketIdentifierCode\":\"XWBO\"");
        var withoutLei = () =>
            source.Resolve(
                market,
                row,
                Firds("AT000000STR1", "XETB", null),
                CancellationToken.None
            );
        await withoutLei.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Euronext_CapturesTheMarketsDirectoryAndConfirmsARowAgainstItsProductPage()
    {
        var html = await Fixture("Euronext", "Lisbon", "directory.html");
        var body = await Fixture("Euronext", "Lisbon", "equities.json");
        var product = await Fixture("Euronext", "Paris", "totalenergies.html");
        var handler = new EuronextDirectoryTestHandler([html, body, product]);
        using var http = new HttpClient(handler);
        var source = new EuronextEquityMarketDirectorySource(new EuronextDirectoryClient(http));
        var lisbon = EquityMarketCatalog.TryGet("euronext-lisbon");
        var paris = EquityMarketCatalog.TryGet("euronext-paris");
        source.SourceKey.Should().Be(lisbon.DirectorySource);
        source.Supports(lisbon).Should().BeTrue();
        source.Supports(EquityMarketCatalog.TryGet("xetra")).Should().BeFalse();
        source.Supports(EquityMarketCatalog.TryGet("lse")).Should().BeFalse();

        var snapshot = await source.Capture(lisbon, CancellationToken.None);

        snapshot.EvidenceSource.Should().Be("euronext-lisbon-directory-v1");
        snapshot.SourceUrl.Should().Be(EuronextMarket.Lisbon.DirectoryUrl);
        snapshot.Rows.Should().HaveCount(49);
        snapshot
            .Rows.Should()
            .AllSatisfy(row =>
                row.StatedPrimaryMarketIdentifierCode.Should()
                    .BeNull(
                        "a line Euronext homes on this market states no primary beyond Euronext"
                    )
            );
        var altri = snapshot.Rows.Single(row => row.Symbol == "ALTR");
        altri.Isin.Should().Be("PTALT0AE0002");
        altri.MarketIdentifierCode.Should().Be("XLIS");
        altri.ReportedCurrency.Should().Be("EUR");
        altri
            .SourceUrl.AbsoluteUri.Should()
            .Be("https://live.euronext.com/en/product/equities/PTALT0AE0002-XLIS");
        snapshot.PayloadJson.Should().Contain("\"MarketSlug\":\"lisbon\"");

        var row = new EquityMarketDirectoryRow
        {
            Isin = "FR0000120271",
            MarketIdentifierCode = "XPAR",
            Symbol = "TTE",
            Name = "TOTALENERGIES",
            ReportedCurrency = "EUR",
            SourceUrl = new Uri("https://live.euronext.com/en/product/equities/FR0000120271-XPAR"),
        };
        var resolved = await source.Resolve(
            paris,
            row,
            Firds("FR0000120271", "XPAR", "529900S21EQ1BO4ESM68"),
            CancellationToken.None
        );
        resolved.SourceIssuerIdentifier.Should().Be("002816");
        resolved.Name.Should().Be("TOTALENERGIES");
        resolved.SourceUrl.Should().Be(row.SourceUrl);
        handler.Requests[2].Url.Should().Be(row.SourceUrl);
    }

    [Fact]
    public async Task Euronext_StatesTheSiblingMarketAsPrimaryOnlyForACrossListedLine()
    {
        var html = await Fixture("Euronext", "Paris", "directory.html");
        var body = await Fixture("Euronext", "Paris", "equities.json");
        using var http = new HttpClient(new EuronextDirectoryTestHandler([html, body]));
        var source = new EuronextEquityMarketDirectorySource(new EuronextDirectoryClient(http));
        var paris = EquityMarketCatalog.TryGet("euronext-paris");

        var snapshot = await source.Capture(paris, CancellationToken.None);

        snapshot.EvidenceSource.Should().Be("euronext-paris-directory-v1");
        snapshot
            .Rows.Select(row =>
                (row.Symbol, row.MarketIdentifierCode, row.StatedPrimaryMarketIdentifierCode)
            )
            .Should()
            .Equal(
                ("ABO", "XPAR", "XBRU"),
                ("AC", "XPAR", null),
                ("ACMC", "XPMC", null),
                ("AF", "XPAR", null),
                ("AI", "XPAR", null)
            );
        var abo = snapshot.Rows.Single(row => row.Symbol == "ABO");
        abo.SourceUrl.AbsolutePath.Should().Be("/en/product/equities/BE0974278104-XBRU");
        EquityMarketDirectoryGate
            .IsHomeShare(paris, abo, Firds("BE0974278104", "XPAR", "549300G6XMDT7F5X8D57"))
            .Should()
            .BeFalse("a line homed on Brussels is not Paris's own share listing");
        snapshot.PayloadJson.Should().Contain("\"PrimaryMarketIdentifierCode\":\"XBRU\"");
    }

    [Fact]
    public async Task Euronext_RefusesAProductPageThatDisagreesWithTheRow()
    {
        var product = await Fixture("Euronext", "Paris", "totalenergies.html");
        using var http = new HttpClient(new EuronextDirectoryTestHandler([product]));
        var source = new EuronextEquityMarketDirectorySource(new EuronextDirectoryClient(http));
        var row = new EquityMarketDirectoryRow
        {
            Isin = "FR0000120271",
            MarketIdentifierCode = "XPAR",
            Symbol = "TOT",
            Name = "TOTALENERGIES",
            SourceUrl = new Uri("https://live.euronext.com/en/product/equities/FR0000120271-XPAR"),
        };
        var resolve = () =>
            source.Resolve(
                EquityMarketCatalog.TryGet("euronext-paris"),
                row,
                Firds("FR0000120271", "XPAR", null),
                CancellationToken.None
            );
        await resolve.Should().ThrowAsync<InvalidDataException>();
    }
}
