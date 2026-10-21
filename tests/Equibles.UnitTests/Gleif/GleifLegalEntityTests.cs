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

    private const string ToyotaLei = "5493006W3QUS5LMH6R84";

    private const string KyokuyoLei = "353800PIEETYXIDK6K51";

    private static Task<string> Toyota(string name) => Recorded("toyota-" + name);

    private static Task<string> Recorded(string name) =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Gleif", name + ".json")
        );

    [Fact]
    public async Task IsinsForLeiReadsTheEntityThenEveryIsinGleifRelatesToIt()
    {
        var handler = new EuronextDirectoryTestHandler([
            await Toyota("lei-record"),
            await Toyota("isins"),
        ]);
        using var http = new HttpClient(handler);

        var identity = await new GleifIdentityClient(
            http,
            NullLogger<GleifIdentityClient>.Instance
        ).GetIsinsForLei(ToyotaLei);

        handler
            .Requests.Select(request => request.Url.AbsoluteUri)
            .Should()
            .Equal(
                "https://api.gleif.org/api/v1/lei-records/" + ToyotaLei,
                "https://api.gleif.org/api/v1/lei-records/"
                    + ToyotaLei
                    + "/isins?page%5Bsize%5D=200"
            );
        identity.LegalEntityIdentifier.Should().Be(ToyotaLei);
        identity.EntityStatus.Should().Be("ACTIVE");
        identity.RelatedIsinCount.Should().Be(22);
        identity.RelatedIsins.Should().HaveCount(22).And.Contain("JP3633400001");
    }

    [Fact]
    public async Task IsinsForLeiStatesNoneWhenGleifRelatesNone()
    {
        var page = JsonNode.Parse(await Toyota("isins"));
        page["meta"]["pagination"]["total"] = 0;
        page["data"] = new JsonArray();
        using var http = new HttpClient(
            new EuronextDirectoryTestHandler([await Toyota("lei-record"), page.ToJsonString()])
        );

        var identity = await new GleifIdentityClient(
            http,
            NullLogger<GleifIdentityClient>.Instance
        ).GetIsinsForLei(ToyotaLei);

        identity.RelatedIsins.Should().BeEmpty();
        identity.RelatedIsinCount.Should().Be(0);
    }

    [Fact]
    public async Task IsinsForLeiReadsTheSecuritiesPageWhenTheRecordLinksNone()
    {
        // GLEIF leaves the isins relationship out of the record of an entity it relates no ISIN to.
        var handler = new EuronextDirectoryTestHandler([
            await Recorded("kyokuyo-lei-record"),
            await Recorded("kyokuyo-isins"),
        ]);
        using var http = new HttpClient(handler);

        var identity = await new GleifIdentityClient(
            http,
            NullLogger<GleifIdentityClient>.Instance
        ).GetIsinsForLei(KyokuyoLei);

        handler
            .Requests.Select(request => request.Url.AbsoluteUri)
            .Should()
            .Equal(
                "https://api.gleif.org/api/v1/lei-records/" + KyokuyoLei,
                "https://api.gleif.org/api/v1/lei-records/"
                    + KyokuyoLei
                    + "/isins?page%5Bsize%5D=200"
            );
        identity.RelatedIsins.Should().BeEmpty();
        identity.RelatedIsinCount.Should().Be(0);
    }

    [Theory]
    [InlineData("other-lei")]
    [InlineData("short-total")]
    [InlineData("other-link")]
    public async Task IsinsForLeiRefusesAPageThatIsNotTheEntitysOwn(string scenario)
    {
        var record = JsonNode.Parse(await Toyota("lei-record"));
        var page = JsonNode.Parse(await Toyota("isins"));
        if (scenario == "other-lei")
            page["data"][0]["attributes"]["lei"] = Lei;
        if (scenario == "short-total")
            page["meta"]["pagination"]["total"] = 23;
        if (scenario == "other-link")
            record["data"]["relationships"]["isins"]["links"]["related"] =
                "https://api.gleif.org/api/v1/lei-records/" + Lei + "/isins";
        using var http = new HttpClient(
            new EuronextDirectoryTestHandler([record.ToJsonString(), page.ToJsonString()])
        );

        var lookup = () =>
            new GleifIdentityClient(http, NullLogger<GleifIdentityClient>.Instance).GetIsinsForLei(
                ToyotaLei
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
