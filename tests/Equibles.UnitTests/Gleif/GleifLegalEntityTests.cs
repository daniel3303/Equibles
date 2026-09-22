using System.Text.Json.Nodes;
using Equibles.Integrations.Gleif;
using Equibles.UnitTests.Euronext;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Gleif;

public class GleifLegalEntityTests
{
    private const string Lei = "743700W8ZIJAMXWWWD26";

    private static Task<string> Fixture() =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Gleif", "aspocomp-lei.json")
        );

    [Fact]
    public async Task ExactLeiLookupConfirmsEntityWithoutClaimingAnIsinRelationship()
    {
        var body = await Fixture();
        var handler = new EuronextDirectoryTestHandler([body]);
        using var http = new HttpClient(handler);
        var identity = await new GleifIdentityClient(
            http,
            NullLogger<GleifIdentityClient>.Instance
        ).GetIssuerForLei(Lei);
        identity.RequestedLei.Should().Be(Lei);
        identity.LegalEntityIdentifier.Should().Be(Lei);
        identity.RequestedIsin.Should().BeNull();
        identity.RelatedIsins.Should().BeEmpty();
        identity.LegalName.Should().Be("Aspocomp Group Oyj");
        identity.EntityStatus.Should().Be("ACTIVE");
        identity.RegistrationStatus.Should().Be("ISSUED");
        identity.ResponseBodies.Should().Equal(body);
        handler.Requests.Should().ContainSingle();
        identity
            .SourceUrl.AbsoluteUri.Should()
            .Be("https://api.gleif.org/api/v1/lei-records/" + Lei);
    }

    [Theory]
    [InlineData("wrong-id")]
    [InlineData("wrong-lei")]
    [InlineData("wrong-type")]
    [InlineData("missing-status")]
    [InlineData("missing-data")]
    [InlineData("array-data")]
    public async Task UnconfirmedOrMalformedIdentityIsRefused(string scenario)
    {
        var body = JsonNode.Parse(await Fixture());
        if (scenario == "wrong-id")
            body["data"]["id"] = "213800AKSTYRLHY3X497";
        if (scenario == "wrong-lei")
            body["data"]["attributes"]["lei"] = "213800AKSTYRLHY3X497";
        if (scenario == "wrong-type")
            body["data"]["type"] = "isins";
        if (scenario == "missing-status")
            body["data"]["attributes"]["entity"].AsObject().Remove("status");
        if (scenario == "missing-data")
            body.AsObject().Remove("data");
        if (scenario == "array-data")
            body["data"] = new JsonArray();
        using var http = new HttpClient(new EuronextDirectoryTestHandler([body.ToJsonString()]));
        var lookup = () =>
            new GleifIdentityClient(http, NullLogger<GleifIdentityClient>.Instance).GetIssuerForLei(
                Lei
            );
        await lookup.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("743700W8ZIJAMXWWWD27")]
    [InlineData("../../other")]
    public async Task InvalidIdentifierNeverMakesARequest(string lei)
    {
        var handler = new EuronextDirectoryTestHandler([]);
        using var http = new HttpClient(handler);
        var lookup = () =>
            new GleifIdentityClient(http, NullLogger<GleifIdentityClient>.Instance).GetIssuerForLei(
                lei
            );
        await lookup.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }
}
