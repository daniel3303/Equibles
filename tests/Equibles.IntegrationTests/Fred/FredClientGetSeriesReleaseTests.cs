using System.Net;
using Equibles.Integrations.Fred;
using Equibles.Integrations.Fred.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// `GetSeriesRelease` resolves the FRED release a series belongs to via
/// `/fred/series/release`. Pin the URL contract (endpoint + series_id) and the
/// JSON field mapping (snake_case `press_release` → PressRelease) so the
/// release-calendar importer keeps linking series to the right release.
/// </summary>
public class FredClientGetSeriesReleaseTests
{
    [Fact]
    public async Task GetSeriesRelease_ParsesReleaseRecord_AndCallsSeriesReleaseEndpoint()
    {
        var payload = """
            {"releases":[{
                "id":10,
                "realtime_start":"2026-06-10",
                "realtime_end":"2026-06-10",
                "name":"Consumer Price Index",
                "press_release":true,
                "link":"http://www.bls.gov/cpi/"
            }]}
            """;
        var handler = new CapturingHandler(payload);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FredOptions { ApiKey = "test-key" });
        var sut = new FredClient(httpClient, Substitute.For<ILogger<FredClient>>(), options);

        var release = await sut.GetSeriesRelease("CPIAUCSL");

        release.Should().NotBeNull();
        release!.Id.Should().Be(10);
        release.Name.Should().Be("Consumer Price Index");
        release.PressRelease.Should().BeTrue();
        release.Link.Should().Be("http://www.bls.gov/cpi/");

        handler.Requests.Should().HaveCount(1);
        var uri = handler.Requests[0].RequestUri!;
        uri.AbsolutePath.Should().Be("/fred/series/release");
        uri.Query.Should().Contain("series_id=CPIAUCSL");
    }

    [Fact]
    public async Task GetSeriesRelease_EmptyReleases_ReturnsNull()
    {
        var handler = new CapturingHandler("""{"releases":[]}""");
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FredOptions { ApiKey = "test-key" });
        var sut = new FredClient(httpClient, Substitute.For<ILogger<FredClient>>(), options);

        var release = await sut.GetSeriesRelease("UNKNOWN");

        release.Should().BeNull();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _payload;
        public List<HttpRequestMessage> Requests { get; } = new();

        public CapturingHandler(string payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_payload) }
            );
        }
    }
}
