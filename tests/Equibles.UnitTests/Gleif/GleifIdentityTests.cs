using System.Net;
using System.Text.Json.Nodes;
using Equibles.Integrations.Gleif;
using Equibles.UnitTests.Euronext;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Gleif;

public class GleifIdentityTests
{
    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Gleif", name));

    private static GleifIdentityClient Client(HttpClient http) =>
        new(http, NullLogger<GleifIdentityClient>.Instance);

    [Fact]
    public async Task CapturedIssuer_ConfirmsTheExactIsinAndRetainsEveryRelatedSecurity()
    {
        var bodies = new[]
        {
            await Fixture("altri-issuer.json"),
            await Fixture("altri-isins.json"),
        };
        var handler = new EuronextDirectoryTestHandler(bodies);
        using var http = new HttpClient(handler);
        var identity = await Client(http).GetIssuerForIsin("PTALT0AE0002");
        identity.LegalEntityIdentifier.Should().Be("213800AKSTYRLHY3X497");
        identity.LegalName.Should().Be("ALTRI, S.G.P.S., S.A.");
        identity.Jurisdiction.Should().Be("PT");
        identity.EntityStatus.Should().Be("ACTIVE");
        identity
            .RelatedIsins.Should()
            .HaveCount(6)
            .And.Contain("PTALT0AE0002")
            .And.Contain("US02209Y1001");
        identity.RelatedIsinCount.Should().Be(6);
        identity.ResponseBodies.Should().Equal(bodies);
        handler.Requests.Should().HaveCount(2);
        handler
            .Requests[1]
            .Url.AbsoluteUri.Should()
            .Be(
                "https://api.gleif.org/api/v1/lei-records/213800AKSTYRLHY3X497/isins?page%5Bsize%5D=200"
            );
    }

    [Fact]
    public async Task RelatedLinkStatingItsOwnPageSize_IsRequestedAsServed()
    {
        var issuer = JsonNode.Parse(await Fixture("altri-issuer.json"));
        issuer["data"][0]["relationships"]["isins"]["links"]["related"] =
            "https://api.gleif.org/api/v1/lei-records/213800AKSTYRLHY3X497/isins?page%5Bsize%5D=50";
        var handler = new EuronextDirectoryTestHandler([
            issuer.ToJsonString(),
            await Fixture("altri-isins.json"),
        ]);
        using var http = new HttpClient(handler);
        await Client(http).GetIssuerForIsin("PTALT0AE0002");
        handler
            .Requests[1]
            .Url.AbsoluteUri.Should()
            .Be(
                "https://api.gleif.org/api/v1/lei-records/213800AKSTYRLHY3X497/isins?page%5Bsize%5D=50"
            );
    }

    [Fact]
    public async Task ThrottledRequest_IsRetriedAfterTheBackoff()
    {
        var handler = new StatusTestHandler([
            (HttpStatusCode.TooManyRequests, ""),
            (HttpStatusCode.OK, await Fixture("altri-issuer.json")),
            (HttpStatusCode.OK, await Fixture("altri-isins.json")),
        ]);
        using var http = new HttpClient(handler);
        var identity = await Client(http).GetIssuerForIsin("PTALT0AE0002");
        identity.LegalEntityIdentifier.Should().Be("213800AKSTYRLHY3X497");
        identity.RelatedIsins.Should().HaveCount(6);
        handler.Requests.Should().HaveCount(3);
        handler
            .Requests.Take(2)
            .Select(request => request.AbsoluteUri)
            .Distinct()
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task IssuerAboveTheEnumerationBound_RecordsOnlyTheRequestedIsinAndTheReportedTotal()
    {
        var related = JsonNode.Parse(await Fixture("altri-isins.json"));
        related["meta"]["pagination"]["total"] = 42_020;
        related["meta"]["pagination"]["lastPage"] = 211;
        related["links"]["next"] =
            "https://api.gleif.org/api/v1/lei-records/213800AKSTYRLHY3X497/isins?page%5Bnumber%5D=2&page%5Bsize%5D=200";
        var handler = new EuronextDirectoryTestHandler([
            await Fixture("altri-issuer.json"),
            related.ToJsonString(),
        ]);
        using var http = new HttpClient(handler);
        var identity = await Client(http).GetIssuerForIsin("PTALT0AE0002");
        identity.LegalEntityIdentifier.Should().Be("213800AKSTYRLHY3X497");
        identity.RelatedIsins.Should().Equal("PTALT0AE0002");
        identity.RelatedIsinCount.Should().Be(42_020);
        identity.ResponseBodies.Should().HaveCount(2);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task NoExactMapping_ReturnsExplicitlyUnknownIssuerWithoutFollowingAName()
    {
        var root = JsonNode.Parse(await Fixture("altri-issuer.json"));
        root["meta"]["pagination"]["total"] = 0;
        root["data"] = new JsonArray();
        var handler = new EuronextDirectoryTestHandler([root.ToJsonString()]);
        using var http = new HttpClient(handler);
        var identity = await Client(http).GetIssuerForIsin("PTALT0AE0002");
        identity.LegalEntityIdentifier.Should().BeNull();
        identity.RelatedIsins.Should().BeEmpty();
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("different-id")]
    [InlineData("foreign-link")]
    [InlineData("other-issuer-link")]
    [InlineData("filtered-link")]
    [InlineData("wrong-related-lei")]
    [InlineData("duplicate-isin")]
    [InlineData("missing-requested-isin")]
    [InlineData("truncated")]
    [InlineData("changed-golden-copy")]
    [InlineData("missing-attributes")]
    [InlineData("wrong-attributes-type")]
    [InlineData("malformed-json")]
    [InlineData("bad-lei-check-digit")]
    [InlineData("bad-related-isin-check-digit")]
    public async Task InvalidOrIncompleteIdentity_IsRefused(string scenario)
    {
        var issuer = JsonNode.Parse(await Fixture("altri-issuer.json"));
        var related = JsonNode.Parse(await Fixture("altri-isins.json"));
        var links = issuer["data"][0]["relationships"]["isins"]["links"];
        if (scenario == "ambiguous")
            issuer["meta"]["pagination"]["total"] = 2;
        if (scenario == "different-id")
            issuer["data"][0]["id"] = "DIFFERENT";
        if (scenario == "foreign-link")
            links["related"] = "https://example.com/api/v1/lei-records/213800AKSTYRLHY3X497/isins";
        if (scenario == "other-issuer-link")
            links["related"] = "https://api.gleif.org/api/v1/lei-records/OTHER/isins";
        if (scenario == "filtered-link")
            links["related"] = links["related"].GetValue<string>() + "?filter[isin]=US02209Y1001";
        if (scenario == "wrong-related-lei")
            related["data"][0]["attributes"]["lei"] = "DIFFERENT";
        if (scenario == "duplicate-isin")
            related["data"][0] = related["data"][1].DeepClone();
        if (scenario == "missing-requested-isin")
            related["data"][3]["attributes"]["isin"] = "PT0000000000";
        if (scenario == "truncated")
            related["meta"]["pagination"]["total"] = 7;
        if (scenario == "changed-golden-copy")
            related["meta"]["goldenCopy"]["publishDate"] = "2026-09-13T00:00:00Z";
        if (scenario == "missing-attributes")
            issuer["data"][0].AsObject().Remove("attributes");
        if (scenario == "wrong-attributes-type")
            issuer["data"][0]["attributes"] = "invalid";
        if (scenario == "bad-lei-check-digit")
        {
            issuer["data"][0]["id"] = "213800AKSTYRLHY3X498";
            issuer["data"][0]["attributes"]["lei"] = "213800AKSTYRLHY3X498";
        }
        if (scenario == "bad-related-isin-check-digit")
            related["data"][0]["attributes"]["isin"] = "US02209Y1000";
        var handler = new EuronextDirectoryTestHandler([
            scenario == "malformed-json" ? "{" : issuer.ToJsonString(),
            related.ToJsonString(),
        ]);
        using var http = new HttpClient(handler);
        var fetch = () => Client(http).GetIssuerForIsin("PTALT0AE0002");
        await fetch.Should().ThrowAsync<InvalidDataException>();
        if (scenario.EndsWith("link"))
            handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Pagination_FollowsEveryValidatedPageBeforePublishingRelationships()
    {
        var issuer = await Fixture("altri-issuer.json");
        var all = JsonNode.Parse(await Fixture("altri-isins.json"));
        var first = all.DeepClone();
        var second = all.DeepClone();
        first["data"] = new JsonArray(
            all["data"].AsArray().Take(3).Select(row => row.DeepClone()).ToArray()
        );
        second["data"] = new JsonArray(
            all["data"].AsArray().Skip(3).Select(row => row.DeepClone()).ToArray()
        );
        first["links"]["next"] =
            "https://api.gleif.org/api/v1/lei-records/213800AKSTYRLHY3X497/isins?page%5Bnumber%5D=2&page%5Bsize%5D=3";
        var handler = new EuronextDirectoryTestHandler([
            issuer,
            first.ToJsonString(),
            second.ToJsonString(),
        ]);
        using var http = new HttpClient(handler);
        var identity = await Client(http).GetIssuerForIsin("PTALT0AE0002");
        identity.RelatedIsins.Should().HaveCount(6);
        identity.RelatedIsinCount.Should().Be(6);
        identity.ResponseBodies.Should().HaveCount(3);
        handler.Requests.Should().HaveCount(3);
        handler
            .Requests[1]
            .Url.AbsoluteUri.Should()
            .Be(
                "https://api.gleif.org/api/v1/lei-records/213800AKSTYRLHY3X497/isins?page%5Bsize%5D=200"
            );
        handler.Requests[2].Url.AbsoluteUri.Should().Be(first["links"]["next"].GetValue<string>());
    }

    // Serves each queued status and body in order so a throttled attempt can be followed by a served one.
    private sealed class StatusTestHandler(
        IEnumerable<(HttpStatusCode Status, string Body)> replies
    ) : HttpMessageHandler
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
