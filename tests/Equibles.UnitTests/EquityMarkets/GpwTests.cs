using Equibles.Integrations.Gpw;
using Equibles.UnitTests.Euronext;

namespace Equibles.UnitTests.EquityMarkets;

public class GpwTests
{
    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "EquityMarkets", "Gpw", name)
        );

    [Fact]
    public async Task Table_ReadsEveryRowByItsColumnClass()
    {
        var rows = GpwParser.ReadTable(await Fixture("quotations.continuous.sample.html"));
        rows.Should().HaveCount(6);
        var studio = rows.Single(row => row.Isin == "PL11BTS00015");
        studio.Name.Should().Be("11BIT");
        studio.Shortcut.Should().Be("11B");
        studio.Currency.Should().Be("PLN");
        studio.MarketIdentifierCode.Should().Be("XWAR");
        rows.Should().AllSatisfy(row => row.MarketIdentifierCode.Should().Be("XWAR"));
        var fix1 = GpwParser.ReadTable(await Fixture("quotations.fix1.html"));
        fix1.Should().HaveCount(3);
        var pbg = fix1.Single(row => row.Isin == "PLPBG0000029");
        pbg.Name.Should().Be("PBG /Z /6,/7", "the table appends the venue's status markers");
        pbg.Shortcut.Should().Be("PBG");
        fix1.Should().Contain(row => row.Isin == "SE0001856519");
        GpwParser.ReadTable(await Fixture("quotations.fix2.sample.html")).Should().HaveCount(3);
        GpwParser
            .ReadTable(await Fixture("quotations.empty.derived.html"))
            .Should()
            .BeEmpty("an auction table with no line that day still proves its shape by its header");
    }

    [Theory]
    [InlineData(
        "link-isin",
        "company-factsheet?isin=PL11BTS00015",
        "company-factsheet?isin=PL11BTS00016"
    )]
    [InlineData("bad-isin", ">PL11BTS00015</td>", ">PL11BTS00016</td>")]
    [InlineData("no-shortcut", ">11B</td>", "></td>")]
    [InlineData(
        "bad-currency",
        ">PLN</td><td class=\"text-center col6\" >16:49:34",
        ">zl</td><td class=\"text-center col6\" >16:49:34"
    )]
    [InlineData(
        "no-mic",
        "<td class=\"left col21\"  >XWAR</td></tr>",
        "<td class=\"left col21\"  ></td></tr>"
    )]
    [InlineData("no-header", "class=\"left col21\">MIC</th>", "class=\"left col22\">MIC</th>")]
    [InlineData("no-table", "<table ", "<div ")]
    public async Task ChangedShapeOrInvalidRow_RefusesTheWholeTable(
        string scenario,
        string original,
        string replacement
    )
    {
        var html = await Fixture("quotations.continuous.sample.html");
        html.Should().Contain(original, scenario);
        var read = () => GpwParser.ReadTable(html.Replace(original, replacement));
        read.Should().Throw<InvalidDataException>(scenario);
    }

    [Fact]
    public async Task Factsheet_StatesTheNameIsinAndShortcutInItsOwnMarkup()
    {
        var sheet = GpwParser.ReadFactsheet(
            await Fixture("company-factsheet.PL11BTS00015.excerpt.html")
        );
        sheet.Name.Should().Be("11BIT");
        sheet.Isin.Should().Be("PL11BTS00015");
        sheet.Shortcut.Should().Be("11B");
        var noHeading = () => GpwParser.ReadFactsheet("<html><body><h1>11BIT</h1></body></html>");
        noHeading.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Client_ReadsTheThreeTablesAndTheCompanyPageFromTheExchangeOriginOnly()
    {
        var handler = new EuronextDirectoryTestHandler([
            await Fixture("quotations.continuous.sample.html"),
            await Fixture("quotations.fix1.html"),
            await Fixture("quotations.fix2.sample.html"),
            await Fixture("company-factsheet.PL11BTS00015.excerpt.html"),
        ]);
        using var http = new HttpClient(handler);
        var client = new GpwClient(http);
        var list = await client.GetQuotations();
        list.Tables.Select(table => table.TradingSystem)
            .Should()
            .Equal("continuous", "fix1", "fix2");
        list.Tables[0].SourceUrl.Query.Should().Contain("start=showTable").And.Contain("type=&");
        list.Tables[1]
            .SourceUrl.Query.Should()
            .Contain("start=listSingle")
            .And.Contain("type=fix1");
        list.Quotations.Should().HaveCount(12);
        var sheet = await client.GetFactsheet("PL11BTS00015");
        sheet
            .SourceUrl.AbsoluteUri.Should()
            .Be("https://www.gpw.pl/company-factsheet?isin=PL11BTS00015");
        handler
            .Requests.Should()
            .AllSatisfy(request =>
            {
                request.Method.Should().Be(HttpMethod.Get);
                request.Url.Host.Should().Be("www.gpw.pl");
            });
        using var other = new HttpClient(
            new EuronextDirectoryTestHandler([
                await Fixture("company-factsheet.PL11BTS00015.excerpt.html"),
            ])
        );
        var mismatch = () => new GpwClient(other).GetFactsheet("PLKGHM000017");
        await mismatch
            .Should()
            .ThrowAsync<InvalidDataException>()
            .WithMessage("*another security*");
    }
}
