using System.Net;
using System.Text.Json.Nodes;
using Equibles.Integrations.Gleif;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Gleif;

public class GleifEntityClientTests
{
    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Gleif", name));

    private static GleifEntityClient Client(HttpClient http) =>
        new(http, NullLogger<GleifEntityClient>.Instance);

    // Samsung Electronics files its legal name in Korean; the English name is only an other name.
    [Fact]
    public async Task Search_ReadsLegalOtherAndTransliteratedNames_WithCommasStrippedFromTheQuery()
    {
        var handler = new ReplyHandler([
            (HttpStatusCode.OK, await Fixture("samsung-electronics-fulltext.json")),
        ]);
        using var http = new HttpClient(handler);

        var search = await Client(http).Search("Samsung Electronics Co., Ltd.");

        handler
            .Requests.Should()
            .Equal(
                new Uri(
                    "https://api.gleif.org/api/v1/lei-records?filter%5Bfulltext%5D=Samsung%20Electronics%20Co.%20Ltd.&page%5Bsize%5D=10"
                )
            );
        search.Total.Should().Be(3);
        search
            .Entities.Select(entity => entity.LegalEntityIdentifier)
            .Should()
            .Equal("9884007ER46L6N7EI764", "988400SM6BU7Q5ZQRV92", "2549000PB5BA9MOMBQ96");
        var parent = search.Entities[0];
        parent.LegalName.Should().Be("삼성전자(주)");
        parent.Country.Should().Be("KR");
        parent.Category.Should().Be("GENERAL");
        parent.EntityStatus.Should().Be("ACTIVE");
        parent
            .OtherNames.Should()
            .ContainSingle(name =>
                name.Name == "SAMSUNG ELECTRONICS CO., LTD" && !name.IsTransliterated
            );
        search
            .Entities[2]
            .OtherNames.Should()
            .Contain(name =>
                name.Name == "SAMSUNG ELECTRONICS TAIWAN CO., LTD." && name.IsTransliterated
            );
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData(" , ", 10)]
    [InlineData("Samsung", 0)]
    [InlineData("Samsung", 21)]
    public async Task Search_RefusesAnEmptyNameOrPageSize_WithoutARequest(string name, int max)
    {
        var handler = new ReplyHandler([]);
        using var http = new HttpClient(handler);

        var search = () => Client(http).Search(name, max);

        await search.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUltimateParent_ReadsTheTopOfTheChain()
    {
        var handler = new ReplyHandler([
            (HttpStatusCode.OK, await Fixture("aws-ultimate-parent.json")),
        ]);
        using var http = new HttpClient(handler);

        var parent = await Client(http).GetUltimateParent("2549000I2PRQGGIGCA75");

        parent.LegalEntityIdentifier.Should().Be("ZXTILKJKG63JELOEG630");
        parent.LegalName.Should().Be("AMAZON.COM, INC.");
        parent.Country.Should().Be("US");
        handler
            .Requests.Should()
            .Equal(
                new Uri(
                    "https://api.gleif.org/api/v1/lei-records/2549000I2PRQGGIGCA75/ultimate-parent"
                )
            );
    }

    [Fact]
    public async Task GetDirectParent_ReadsTheImmediateParent()
    {
        using var http = new HttpClient(
            new ReplyHandler([(HttpStatusCode.OK, await Fixture("google-direct-parent.json"))])
        );

        var parent = await Client(http).GetDirectParent("7ZW8QJWVPR4P1J1KQY45");

        parent.LegalEntityIdentifier.Should().Be("5493006MHB84DD0ZWV18");
    }

    // GLEIF answers 404 for an entity that reports no parent, such as Alphabet itself.
    [Fact]
    public async Task GetUltimateParent_ReadsNotFoundAsNoParent()
    {
        using var http = new HttpClient(
            new ReplyHandler([
                (HttpStatusCode.NotFound, await Fixture("alphabet-ultimate-parent-404.json")),
            ])
        );

        (await Client(http).GetUltimateParent("5493006MHB84DD0ZWV18")).Should().BeNull();
    }

    [Fact]
    public async Task GetUltimateParent_RefusesAnEntityNamedAsItsOwnParent()
    {
        using var http = new HttpClient(
            new ReplyHandler([(HttpStatusCode.OK, await Fixture("aws-ultimate-parent.json"))])
        );

        var parent = () => Client(http).GetUltimateParent("ZXTILKJKG63JELOEG630");

        await parent.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("wrong-id")]
    [InlineData("wrong-type")]
    [InlineData("missing-country")]
    public async Task GetDirectParent_RefusesARecordThatDisagreesWithItself(string scenario)
    {
        var body = JsonNode.Parse(await Fixture("google-direct-parent.json"));
        if (scenario == "wrong-id")
            body["data"]["id"] = "ZXTILKJKG63JELOEG630";
        if (scenario == "wrong-type")
            body["data"]["type"] = "isins";
        if (scenario == "missing-country")
            body["data"]["attributes"]["entity"]["legalAddress"].AsObject().Remove("country");
        using var http = new HttpClient(
            new ReplyHandler([(HttpStatusCode.OK, body.ToJsonString())])
        );

        var parent = () => Client(http).GetDirectParent("7ZW8QJWVPR4P1J1KQY45");

        await parent.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task GetEntity_RefusesAnotherEntitysRecord()
    {
        using var http = new HttpClient(
            new ReplyHandler([(HttpStatusCode.OK, await Fixture("aws-ultimate-parent.json"))])
        );

        var entity = () => Client(http).GetEntity("2549000I2PRQGGIGCA75");

        await entity.Should().ThrowAsync<InvalidDataException>();
    }

    // Serves each queued status and body in order.
    private sealed class ReplyHandler(IEnumerable<(HttpStatusCode Status, string Body)> replies)
        : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new(replies);
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri);
            if (_replies.Count == 0)
                throw new InvalidOperationException("Unexpected HTTP request.");
            var reply = _replies.Dequeue();
            return Task.FromResult(
                new HttpResponseMessage(reply.Status)
                {
                    Content = new StringContent(reply.Body),
                    RequestMessage = request,
                }
            );
        }
    }
}
