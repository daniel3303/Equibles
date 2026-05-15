using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins SecEdgarClient.GetArchiveFilings' toDate window-skip branch (lines
/// 224-230, zero-hit). SEC paginates older filings into per-range archive JSON
/// files; an archive whose <c>filingFrom</c> is after the requested
/// <c>toDate</c> must be skipped WITHOUT a second HTTP fetch. A regression
/// dropping that guard would download every historical archive on every CIK
/// sweep — needless SEC API load and rate-limit risk.
/// </summary>
public class SecEdgarClientGetArchiveFilingsToDateSkipTests
{
    [Fact]
    public async Task GetCompanyFilings_ArchiveFileStartsAfterToDate_SkipsArchiveWithoutFetchingIt()
    {
        // recent has no filings; one archive file entirely after the toDate window.
        var json =
            "{\"cik\":\"1234567\",\"filings\":{\"recent\":{},"
            + "\"files\":[{\"name\":\"CIK0001234567-submissions-001.json\","
            + "\"filingFrom\":\"2030-01-01\",\"filingTo\":\"2031-12-31\"}]}}";
        var handler = new CountingJsonHandler(json);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(handler),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var result = await sut.GetCompanyFilings("0001234567", toDate: new DateOnly(2020, 12, 31));

        // Exactly one HTTP call (the submissions index) — the out-of-window
        // archive was pruned, not fetched a second time.
        result.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    private sealed class CountingJsonHandler : HttpMessageHandler
    {
        private readonly string _json;
        public int CallCount { get; private set; }

        public CountingJsonHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_json) }
            );
        }
    }
}
