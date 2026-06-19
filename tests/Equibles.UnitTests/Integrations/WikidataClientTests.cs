using System.Net;
using System.Text;
using Equibles.Integrations.Wikidata;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Integrations;

/// <summary>
/// Contract: <c>WikidataClient.GetOfficialWebsitesByCik</c> pads CIKs to
/// Wikidata's 10-digit P5531 format for the query but keys results by the CIK as
/// passed, picks the canonical root among P856's many localised variants (the
/// shortest URL, ordinal tie-break), chunks large inputs into bounded queries,
/// and never queries non-digit CIKs.
/// </summary>
public class WikidataClientTests
{
    private static string SparqlJson(params (string Cik, string Website)[] rows)
    {
        var bindings = rows.Select(r => new
        {
            cik = new { type = "literal", value = r.Cik },
            website = new { type = "uri", value = r.Website },
        });
        return JsonConvert.SerializeObject(new { results = new { bindings } });
    }

    private static (WikidataClient Client, RecordingHandler Handler) BuildSut(
        params string[] responses
    )
    {
        var handler = new RecordingHandler(responses);
        return (
            new WikidataClient(new HttpClient(handler), Substitute.For<ILogger<WikidataClient>>()),
            handler
        );
    }

    [Fact]
    public async Task BareCik_IsPaddedForTheQuery_AndKeysTheResultAsPassed()
    {
        var (client, handler) = BuildSut(SparqlJson(("0000320193", "https://apple.com/")));

        var result = await client.GetOfficialWebsitesByCik(["320193"], CancellationToken.None);

        handler.RequestedQueries.Should().ContainSingle().Which.Should().Contain("\"0000320193\"");
        result
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, string>("320193", "https://apple.com/"));
    }

    [Fact]
    public async Task ManyLocalisedWebsites_ShortestUrlWins()
    {
        var (client, _) = BuildSut(
            SparqlJson(
                ("0000320193", "https://apple.com/de/"),
                ("0000320193", "https://apple.com/"),
                ("0000320193", "https://apple.com.cn/")
            )
        );

        var result = await client.GetOfficialWebsitesByCik(["320193"], CancellationToken.None);

        result["320193"].Should().Be("https://apple.com/");
    }

    [Fact]
    public async Task EqualLengthWebsites_OrdinallySmallestWins_RegardlessOfBindingOrder()
    {
        // Contract: among equal-length candidates the pick is the ordinally-smallest URL,
        // deterministically — so binding order must not change the result.
        var (client, _) = BuildSut(
            SparqlJson(
                ("0000320193", "https://zzz.example/"),
                ("0000320193", "https://aaa.example/")
            )
        );

        var result = await client.GetOfficialWebsitesByCik(["320193"], CancellationToken.None);

        result["320193"].Should().Be("https://aaa.example/");
    }

    [Fact]
    public async Task UnknownCiks_AreAbsentFromTheResult()
    {
        var (client, _) = BuildSut(SparqlJson(("0000320193", "https://apple.com/")));

        var result = await client.GetOfficialWebsitesByCik(
            ["320193", "999999"],
            CancellationToken.None
        );

        result.Should().HaveCount(1).And.ContainKey("320193");
    }

    [Fact]
    public async Task NonDigitCiks_AreNeverQueried()
    {
        var (client, handler) = BuildSut(SparqlJson());

        var result = await client.GetOfficialWebsitesByCik(
            ["abc", "", null, "12 34"],
            CancellationToken.None
        );

        result.Should().BeEmpty();
        handler.RequestedQueries.Should().BeEmpty("nothing digit-shaped was left to query");
    }

    [Fact]
    public async Task MoreCiksThanChunkSize_AreSplitAcrossQueries()
    {
        var (client, handler) = BuildSut(SparqlJson(), SparqlJson());
        var ciks = Enumerable.Range(1, 201).Select(i => i.ToString()).ToList();

        await client.GetOfficialWebsitesByCik(ciks, CancellationToken.None);

        handler.RequestedQueries.Should().HaveCount(2);
        handler.RequestedQueries[0].Should().Contain("\"0000000001\"");
        handler.RequestedQueries[1].Should().Contain("\"0000000201\"");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public RecordingHandler(IEnumerable<string> responses) => _responses = new(responses);

        public List<string> RequestedQueries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestedQueries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            var body = _responses.Count > 0 ? _responses.Dequeue() : "{}";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        body,
                        Encoding.UTF8,
                        "application/sparql-results+json"
                    ),
                }
            );
        }
    }
}
