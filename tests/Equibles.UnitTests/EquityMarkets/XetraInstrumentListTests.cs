using Equibles.Integrations.Xetra;
using Equibles.UnitTests.Euronext;

namespace Equibles.UnitTests.EquityMarkets;

public class XetraInstrumentListTests
{
    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "EquityMarkets", "Xetra", name)
        );

    [Fact]
    public async Task Page_LinksExactlyOneInstrumentFileWhoseHashIsNeverComposed()
    {
        var html = await Fixture("tradable-instruments-page.html");
        XetraInstrumentListParser
            .ReadDownloadPath(html)
            .Should()
            .MatchRegex(@"^/resource/blob/\d+/[0-9a-f]+/data/t7-xetr-allTradableInstruments\.csv$");
        XetraInstrumentListParser
            .ReadDownloadPath(await Fixture("tradable-instruments-page.absolute-link.excerpt.html"))
            .Should()
            .Be(
                "/resource/blob/1528/2dd6b44cb9d8475ac114e10e873a1858/data/t7-xetr-allTradableInstruments.csv",
                "a cache node that writes the link absolute names the same file"
            );
        XetraInstrumentListParser
            .ReadDownloadPath(
                html + await Fixture("tradable-instruments-page.absolute-link.excerpt.html")
            )
            .Should()
            .Be(XetraInstrumentListParser.ReadDownloadPath(html), "both spellings name one file");
        var none = () => XetraInstrumentListParser.ReadDownloadPath("<html></html>");
        none.Should().Throw<InvalidDataException>();
        var elsewhere = () =>
            XetraInstrumentListParser.ReadDownloadPath(
                "<a href=\"https://example.com/resource/blob/1528/ffff/data/t7-xetr-allTradableInstruments.csv\">x</a>"
            );
        elsewhere.Should().Throw<InvalidDataException>();
        var two = () =>
            XetraInstrumentListParser.ReadDownloadPath(
                html
                    + "<a href=\"/resource/blob/1528/ffff/data/t7-xetr-allTradableInstruments.csv\">x</a>"
            );
        two.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task File_ReadsEveryRowAndKeepsOnlyTheParserNeutralFacts()
    {
        var list = XetraInstrumentListParser.Read(
            await Fixture("t7-xetr-allTradableInstruments.sample.csv")
        );
        list.MarketIdentifierCode.Should().Be("XETR");
        list.LastUpdate.Should().Be(new DateOnly(2026, 9, 15));
        list.Instruments.Should().HaveCount(36);
        list.Instruments.Select(row => row.InstrumentType)
            .Distinct()
            .Should()
            .BeEquivalentTo(["CS", "ETF", "ETN"]);
        var shares = list.Instruments.Where(row => row.InstrumentType == "CS").ToList();
        shares.Should().HaveCount(31);
        shares.Count(row => row.InstrumentStatus == "Active").Should().Be(30);
        shares
            .Single(row => row.InstrumentStatus != "Active")
            .InstrumentStatus.Should()
            .Be("PendingDeletion");
        var strabag = shares.Single(row => row.Isin == "AT000000STR1");
        strabag.Mnemonic.Should().Be("XD4");
        strabag.Name.Should().Be("STRABAG SE");
        strabag.Currency.Should().Be("EUR");
        strabag.PrimaryMarketIdentifierCode.Should().Be("XWBO");
        strabag.MarketIdentifierCode.Should().Be("XETR");
    }

    [Theory]
    [InlineData("preamble")]
    [InlineData("missing-column")]
    [InlineData("other-market")]
    [InlineData("bad-isin")]
    [InlineData("no-symbol")]
    [InlineData("duplicate")]
    [InlineData("short-row")]
    [InlineData("bad-primary-market")]
    public async Task ChangedShapeOrInvalidShareIdentity_RefusesTheWholeFile(string scenario)
    {
        var lines = (await Fixture("t7-xetr-allTradableInstruments.sample.csv"))
            .Split('\n')
            .ToList();
        var header = lines[2].Split(';').ToList();
        var isin = header.IndexOf("ISIN");
        var mnemonic = header.IndexOf("Mnemonic");
        var mic = header.IndexOf("MIC Code");
        var primary = header.IndexOf("Primary Market MIC Code");
        string[] Cells(int line) => lines[line].Split(';');
        void Set(int line, int column, string value)
        {
            var cells = Cells(line);
            cells[column] = value;
            lines[line] = string.Join(';', cells);
        }
        switch (scenario)
        {
            case "preamble":
                lines[1] = "Updated:;2026-09-15";
                break;
            case "missing-column":
                lines[2] = lines[2].Replace("Mnemonic", "Symbol");
                break;
            case "other-market":
                Set(3, mic, "XFRA");
                break;
            case "bad-isin":
                Set(3, isin, "AT000000STR2");
                break;
            case "no-symbol":
                Set(3, mnemonic, "");
                break;
            case "duplicate":
                lines[4] = lines[3];
                break;
            case "short-row":
                lines[3] = lines[3][..lines[3].LastIndexOf(';')];
                break;
            case "bad-primary-market":
                Set(3, primary, "XWB");
                break;
        }
        var parse = () => XetraInstrumentListParser.Read(string.Join('\n', lines));
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task AByteOrderMark_NeverReachesThePreambleCheck()
    {
        var csv = await Fixture("t7-xetr-allTradableInstruments.sample.csv");
        var list = XetraInstrumentListParser.Read("\uFEFF" + csv);
        list.MarketIdentifierCode.Should().Be("XETR");
        list.Instruments.Should().HaveCount(36);
    }

    [Fact]
    public async Task ABlankPrimaryMarket_IsReadAsUnstatedRatherThanRefused()
    {
        var lines = (await Fixture("t7-xetr-allTradableInstruments.sample.csv"))
            .Split('\n')
            .ToList();
        var header = lines[2].Split(';').ToList();
        var cells = lines[3].Split(';');
        cells[header.IndexOf("Primary Market MIC Code")] = "";
        lines[3] = string.Join(';', cells);
        var list = XetraInstrumentListParser.Read(string.Join('\n', lines));
        list.Instruments[0].PrimaryMarketIdentifierCode.Should().BeNull();
        list.Instruments[1].PrimaryMarketIdentifierCode.Should().Be("XWBO");
    }

    [Fact]
    public async Task Client_ReadsThePageThenTheLinkedFileOnTheSameOrigin()
    {
        var page = await Fixture("tradable-instruments-page.html");
        var csv = await Fixture("t7-xetr-allTradableInstruments.sample.csv");
        var handler = new EuronextDirectoryTestHandler([page, csv]);
        using var http = new HttpClient(handler);
        var list = await new XetraInstrumentListClient(http).GetInstruments();
        list.Instruments.Should().HaveCount(36);
        list.PageUrl.AbsoluteUri.Should()
            .Be(
                "https://www.cashmarket.deutsche-boerse.com/cash-en/trading/Tradable-Instruments-Xetra"
            );
        list.SourceUrl.Host.Should().Be("www.cashmarket.deutsche-boerse.com");
        list.SourceUrl.AbsolutePath.Should().EndWith("/data/t7-xetr-allTradableInstruments.csv");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].Url.Should().Be(list.SourceUrl);
    }
}
