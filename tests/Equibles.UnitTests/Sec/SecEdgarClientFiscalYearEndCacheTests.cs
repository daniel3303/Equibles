using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the cache-priming contract added with fiscal-year detection:
/// <c>GetCompanyMetadata</c> now stores the submissions payload so an
/// immediately-following <c>GetCompanyFilings</c> for the SAME CIK is served
/// from cache (net-zero extra SEC request — the whole point of fetching
/// metadata in the scraper). Critically, the cache is URL-keyed, so priming
/// it for one CIK must NEVER serve another CIK's filings — a regression that
/// dropped the URL guard would silently attribute Apple's 10-Ks to whichever
/// company's metadata happened to be fetched last.
/// </summary>
public class SecEdgarClientFiscalYearEndCacheTests
{
    private const string CikA = "0000111111";
    private const string CikB = "0000222222";

    private static string Submissions(string cik, string fiscalYearEnd, bool withFiling)
    {
        var recent = withFiling
            ? "{\"accessionNumber\":[\"0000111111-25-000001\"],"
                + "\"filingDate\":[\"2025-01-15\"],\"reportDate\":[\"2024-12-31\"],"
                + "\"form\":[\"10-K\"],\"primaryDocument\":[\"a.htm\"],"
                + "\"primaryDocDescription\":[\"10-K\"]}"
            : "{}";
        return $"{{\"cik\":\"{cik}\",\"fiscalYearEnd\":\"{fiscalYearEnd}\","
            + $"\"filings\":{{\"recent\":{recent}}}}}";
    }

    private static SecEdgarClient BuildClient(PerUrlHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        return new SecEdgarClient(
            new HttpClient(handler),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );
    }

    [Fact]
    public async Task GetCompanyFilings_SameCikAfterGetCompanyMetadata_ServedFromCacheNoSecondFetch()
    {
        var handler = new PerUrlHandler(
            new() { [$"/submissions/CIK{CikA}.json"] = Submissions(CikA, "0331", withFiling: true) }
        );
        var sut = BuildClient(handler);

        var metadata = await sut.GetCompanyMetadata(CikA);
        var filings = await sut.GetCompanyFilings(CikA);

        metadata.FiscalYearEndMonth.Should().Be(3);
        filings.Should().ContainSingle();
        handler
            .CallCountFor($"/submissions/CIK{CikA}.json")
            .Should()
            .Be(1, "the filings call must reuse the payload the metadata call cached");
    }

    [Fact]
    public async Task GetCompanyFilings_DifferentCikAfterGetCompanyMetadata_DoesNotServeTheOtherCik()
    {
        // Metadata primed for B (no filings); filings then requested for A.
        var handler = new PerUrlHandler(
            new()
            {
                [$"/submissions/CIK{CikB}.json"] = Submissions(CikB, "0928", withFiling: false),
                [$"/submissions/CIK{CikA}.json"] = Submissions(CikA, "0331", withFiling: true),
            }
        );
        var sut = BuildClient(handler);

        await sut.GetCompanyMetadata(CikB);
        var filings = await sut.GetCompanyFilings(CikA);

        // A's single filing proves A's URL was fetched and A's body parsed —
        // the B-primed cache was correctly bypassed on the URL mismatch.
        filings.Should().ContainSingle().Which.Cik.Should().Be(CikA);
        handler.CallCountFor($"/submissions/CIK{CikA}.json").Should().Be(1);
    }

    private sealed class PerUrlHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _bodyByPath;
        private readonly Dictionary<string, int> _calls = new();

        public PerUrlHandler(Dictionary<string, string> bodyByPath) => _bodyByPath = bodyByPath;

        public int CallCountFor(string path) => _calls.TryGetValue(path, out var n) ? n : 0;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            _calls[path] = CallCountFor(path) + 1;

            if (!_bodyByPath.TryGetValue(path, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
        }
    }
}
