using System.Text.Json.Nodes;
using Equibles.Integrations.Gleif;
using Equibles.UnitTests.Euronext;

namespace Equibles.UnitTests.Gleif;

public class GleifIdentityTests
{
    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Gleif", name));

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
        var identity = await new GleifIdentityClient(http).GetIssuerForIsin("PTALT0AE0002");
        identity.LegalEntityIdentifier.Should().Be("213800AKSTYRLHY3X497");
        identity.LegalName.Should().Be("ALTRI, S.G.P.S., S.A.");
        identity.Jurisdiction.Should().Be("PT");
        identity.EntityStatus.Should().Be("ACTIVE");
        identity
            .RelatedIsins.Should()
            .HaveCount(6)
            .And.Contain("PTALT0AE0002")
            .And.Contain("US02209Y1001");
        identity.ResponseBodies.Should().Equal(bodies);
        handler.Requests.Should().HaveCount(2);
        handler
            .Requests[1]
            .Url.AbsoluteUri.Should()
            .Be("https://api.gleif.org/api/v1/lei-records/213800AKSTYRLHY3X497/isins");
    }

    [Fact]
    public async Task NoExactMapping_ReturnsExplicitlyUnknownIssuerWithoutFollowingAName()
    {
        var root = JsonNode.Parse(await Fixture("altri-issuer.json"));
        root["meta"]["pagination"]["total"] = 0;
        root["data"] = new JsonArray();
        var handler = new EuronextDirectoryTestHandler([root.ToJsonString()]);
        using var http = new HttpClient(handler);
        var identity = await new GleifIdentityClient(http).GetIssuerForIsin("PTALT0AE0002");
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
        var fetch = () => new GleifIdentityClient(http).GetIssuerForIsin("PTALT0AE0002");
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
        var identity = await new GleifIdentityClient(http).GetIssuerForIsin("PTALT0AE0002");
        identity.RelatedIsins.Should().HaveCount(6);
        identity.ResponseBodies.Should().HaveCount(3);
        handler.Requests.Should().HaveCount(3);
    }
}
