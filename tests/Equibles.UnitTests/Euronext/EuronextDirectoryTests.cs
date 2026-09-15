using System.Text.Json.Nodes;
using Equibles.Integrations.Euronext;

namespace Equibles.UnitTests.Euronext;

public class EuronextDirectoryTests
{
    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Euronext", "Lisbon", name)
        );

    private static Task<string> ParisFixture(string name) =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Euronext", "Paris", name)
        );

    [Fact]
    public async Task CrossListedRows_KeepTheMarketsOwnVenueAndStateTheLinkedPrimaryVenue()
    {
        var page = EuronextDirectoryParser.ReadPage(
            await ParisFixture("equities.json"),
            EuronextMarket.Paris
        );
        page.TotalRecords.Should().Be(5);
        page.Listings.Select(row =>
                (
                    row.Symbol,
                    row.MarketIdentifierCode,
                    row.PrimaryMarketIdentifierCode,
                    row.SourceUrl.AbsolutePath
                )
            )
            .Should()
            .Equal(
                ("ABO", "XPAR", "XBRU", "/en/product/equities/BE0974278104-XBRU"),
                ("AC", "XPAR", "XPAR", "/en/product/equities/FR0000120404-XPAR"),
                ("ACMC", "XPMC", "XPMC", "/en/product/equities/FR0000120404-XPMC"),
                ("AF", "XPAR", "XPAR", "/en/product/equities/FR001400J770-XPAR"),
                ("AI", "XPAR", "XPAR", "/en/product/equities/FR0000120073-XPAR")
            );
        page.Listings.Select(row => row.ReportedCurrency)
            .Should()
            .Equal("EUR", "EUR", "USD", "EUR", "EUR");
    }

    [Fact]
    public async Task CrossListedRows_ToleratePaddedVenueSeparators()
    {
        var root = JsonNode.Parse(await ParisFixture("equities.json"));
        root["aaData"][0][3] = "<div class=\"nowrap pointer\">XBRU,&nbsp;XPAR</div>";
        root["aaData"][3][3] = "<div class=\"nowrap pointer\">XPAR ,\n XAMS</div>";
        var page = EuronextDirectoryParser.ReadPage(root.ToJsonString(), EuronextMarket.Paris);
        page.Listings[0].MarketIdentifierCode.Should().Be("XPAR");
        page.Listings[0].PrimaryMarketIdentifierCode.Should().Be("XBRU");
        page.Listings[3].MarketIdentifierCode.Should().Be("XPAR");
        page.Listings[3].PrimaryMarketIdentifierCode.Should().Be("XPAR");
    }

    [Theory]
    [InlineData("no-own-venue")]
    [InlineData("two-own-venues")]
    [InlineData("repeated-venue")]
    [InlineData("unknown-venue")]
    [InlineData("link-outside-cell")]
    public async Task CrossListedRows_WithoutOneOwnVenueOrALinkedVenue_AreRefused(string scenario)
    {
        var root = JsonNode.Parse(await ParisFixture("equities.json"));
        var abo = root["aaData"][0];
        if (scenario == "no-own-venue")
            abo[3] = "XBRU, XAMS";
        if (scenario == "two-own-venues")
            abo[3] = "XBRU, XPAR, ALXP";
        if (scenario == "repeated-venue")
            abo[3] = "XBRU, XPAR, XPAR";
        if (scenario == "unknown-venue")
            abo[3] = "XBRU, XPAR, XLON";
        if (scenario == "link-outside-cell")
            abo[0] = "<a href='/en/product/equities/BE0974278104-XAMS'>ABO GROUP</a>";
        var parse = () =>
            EuronextDirectoryParser.ReadPage(root.ToJsonString(), EuronextMarket.Paris);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(2467, true)]
    [InlineData(5000, true)]
    [InlineData(5001, false)]
    public async Task ReportedTotal_IsBoundedAboveMilansDirectorySize(int total, bool accepted)
    {
        var root = JsonNode.Parse(await ParisFixture("equities.json"));
        root["iTotalRecords"] = total;
        root["iTotalDisplayRecords"] = total;
        var parse = () =>
            EuronextDirectoryParser.ReadPage(root.ToJsonString(), EuronextMarket.Paris);
        if (accepted)
            parse().TotalRecords.Should().Be(total);
        else
            parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task CapturedDirectory_ContainsEveryReportedListingAndItsExactSourceIdentity()
    {
        var html = await Fixture("directory.html");
        var body = await Fixture("equities.json");
        var handler = new EuronextDirectoryTestHandler([html, body]);
        using var http = new HttpClient(handler);
        var snapshot = await new EuronextDirectoryClient(http).GetEquities(EuronextMarket.Lisbon);
        snapshot.Listings.Should().HaveCount(49);
        snapshot
            .Listings.GroupBy(row => row.MarketIdentifierCode)
            .ToDictionary(group => group.Key, group => group.Count())
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, int>
                {
                    ["XLIS"] = 33,
                    ["ENXL"] = 15,
                    ["ALXL"] = 1,
                }
            );
        var altri = snapshot.Listings.Single(row => row.Symbol == "ALTR");
        altri.Isin.Should().Be("PTALT0AE0002");
        altri.MarketIdentifierCode.Should().Be("XLIS");
        altri.ReportedCurrency.Should().Be("EUR");
        altri
            .SourceUrl.AbsoluteUri.Should()
            .Be("https://live.euronext.com/en/product/equities/PTALT0AE0002-XLIS");
        snapshot.Listings.Single(row => row.Symbol == "MLVDN").ReportedCurrency.Should().BeNull();
        snapshot.DirectoryHtml.Should().Be(html);
        snapshot.ResponseBodies.Should().Equal(body);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler
            .Requests[1]
            .Body.Should()
            .Contain("iDisplayStart=0")
            .And.Contain("iDisplayLength=100");
    }

    [Fact]
    public async Task Pagination_RequestsEveryOffsetAndPreservesAllRawPages()
    {
        var html = await Fixture("directory.html");
        var root = JsonNode.Parse(await Fixture("equities.json"));
        var rows = root["aaData"].AsArray().Select(row => row.DeepClone()).ToList();
        var bodies = new List<string>();
        foreach (var range in new[] { (0, 20), (20, 20), (40, 9) })
        {
            var page = root.DeepClone();
            page["aaData"] = new JsonArray(
                rows.Skip(range.Item1).Take(range.Item2).Select(row => row.DeepClone()).ToArray()
            );
            bodies.Add(page.ToJsonString());
        }
        var handler = new EuronextDirectoryTestHandler(new[] { html }.Concat(bodies));
        using var http = new HttpClient(handler);
        var snapshot = await new EuronextDirectoryClient(http).GetEquities(EuronextMarket.Lisbon);
        snapshot.Listings.Should().HaveCount(49);
        snapshot.ResponseBodies.Should().Equal(bodies);
        handler
            .Requests.Skip(1)
            .Select(row => row.Body.Split('&')[0])
            .Should()
            .Equal("iDisplayStart=0", "iDisplayStart=20", "iDisplayStart=40");
    }

    [Theory]
    [InlineData("repeat")]
    [InlineData("empty")]
    [InlineData("changed-total")]
    [InlineData("ambiguous-symbol")]
    public async Task IncompleteOrConflictingPagination_NeverReturnsASnapshot(string scenario)
    {
        var html = await Fixture("directory.html");
        var root = JsonNode.Parse(await Fixture("equities.json"));
        var rows = root["aaData"].AsArray().Select(row => row.DeepClone()).ToArray();
        var first = root.DeepClone();
        first["aaData"] = new JsonArray(rows.Take(20).Select(row => row.DeepClone()).ToArray());
        var next = root.DeepClone();
        next["aaData"] = new JsonArray(rows.Skip(20).Select(row => row.DeepClone()).ToArray());
        if (scenario == "repeat")
            next = first.DeepClone();
        if (scenario == "empty")
            next["aaData"] = new JsonArray();
        if (scenario == "changed-total")
        {
            next["iTotalRecords"] = 50;
            next["iTotalDisplayRecords"] = 50;
        }
        if (scenario == "ambiguous-symbol")
        {
            next["aaData"][0][2] = first["aaData"][0][2].DeepClone();
            next["aaData"][0][3] = first["aaData"][0][3].DeepClone();
        }
        using var http = new HttpClient(
            new EuronextDirectoryTestHandler([html, first.ToJsonString(), next.ToJsonString()])
        );
        Func<Task> fetch = () =>
            new EuronextDirectoryClient(http).GetEquities(EuronextMarket.Lisbon);
        await fetch.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("filtered")]
    [InlineData("empty")]
    [InlineData("wrong-market")]
    [InlineData("wrong-link")]
    [InlineData("external-link")]
    [InlineData("duplicate")]
    [InlineData("invalid-isin")]
    [InlineData("missing-column")]
    public async Task InvalidSourceRows_AreRefusedWithoutDroppingIndividualRows(string scenario)
    {
        var root = JsonNode.Parse(await Fixture("equities.json"));
        var rows = root["aaData"].AsArray();
        if (scenario == "filtered")
            root["iTotalDisplayRecords"] = 48;
        if (scenario == "empty")
        {
            root["iTotalRecords"] = 0;
            root["iTotalDisplayRecords"] = 0;
            rows.Clear();
        }
        if (scenario == "wrong-market")
            rows[0][3] = "XPAR";
        if (scenario == "wrong-link")
            rows[0][0] = "<a href='/en/product/equities/PTALT0AE0002-ENXL'>ALTRI</a>";
        if (scenario == "external-link")
            rows[0][0] =
                "<a href='https://example.com/en/product/equities/PTALT0AE0002-XLIS'>ALTRI</a>";
        if (scenario == "duplicate")
            rows[1] = rows[0].DeepClone();
        if (scenario == "invalid-isin")
            rows[0][1] = "PTALT0AE0003";
        if (scenario == "missing-column")
            rows[0].AsArray().RemoveAt(6);
        var parse = () =>
            EuronextDirectoryParser.ReadPage(root.ToJsonString(), EuronextMarket.Lisbon);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(
        "https://example.com/en/product_directory/data/stocks-lisbon?mics=ALXL%2CENXL%2CXLIS"
    )]
    [InlineData("/en/product_directory/data/stocks-paris?mics=ALXL%2CENXL%2CXLIS")]
    [InlineData("/en/product_directory/data/stocks-lisbon?mics=XLIS")]
    [InlineData("/en/product_directory/data/stocks-lisbon?mics=ALXL%2CENXL%2CXLIS&region=PT")]
    public void Gateway_RefusesDifferentOriginsOrFilteredMarketCoverage(string gateway)
    {
        var settings = new
        {
            jsongateway = gateway,
            datapoints = new[]
            {
                "name",
                "isin",
                "symbol",
                "market",
                "lastPrice",
                "precentDayChange",
                "lastTradeTime",
            },
        };
        var html =
            "<script data-drupal-selector='drupal-settings-json'>"
            + System.Text.Json.JsonSerializer.Serialize(settings)
            + "</script>";
        var parse = () => EuronextDirectoryParser.ReadGateway(html, EuronextMarket.Lisbon);
        parse.Should().Throw<InvalidDataException>();
    }
}
