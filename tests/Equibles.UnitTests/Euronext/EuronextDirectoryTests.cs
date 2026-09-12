using System.Text.Json.Nodes;
using Equibles.Integrations.Euronext;

namespace Equibles.UnitTests.Euronext;

public class EuronextDirectoryTests
{
    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Euronext", "Lisbon", name)
        );

    [Fact]
    public async Task CapturedDirectory_ContainsEveryReportedListingAndItsExactSourceIdentity()
    {
        var html = await Fixture("directory.html");
        var body = await Fixture("equities.json");
        var handler = new EuronextDirectoryTestHandler([html, body]);
        using var http = new HttpClient(handler);
        var snapshot = await new EuronextDirectoryClient(http).GetLisbonEquities();
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
        var snapshot = await new EuronextDirectoryClient(http).GetLisbonEquities();
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
        Func<Task> fetch = () => new EuronextDirectoryClient(http).GetLisbonEquities();
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
        var parse = () => EuronextDirectoryParser.ReadLisbonPage(root.ToJsonString());
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
        var parse = () => EuronextDirectoryParser.ReadLisbonGateway(html);
        parse.Should().Throw<InvalidDataException>();
    }
}
