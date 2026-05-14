using System.Net;
using System.Text;
using Equibles.Integrations.Fred;
using Equibles.Integrations.Fred.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// Sibling to <see cref="FredClientTests"/>. That file pins the observations pagination.
/// This pins <see cref="FredClient.GetSeriesMetadata"/> — the other public HTTP method,
/// otherwise uncovered. The metadata endpoint returns a JSON envelope keyed
/// <c>"seriess"</c> (typo preserved by FRED); a regression that renamed the
/// <c>FredSeriesResponse.Series</c> binding away from that exact key would silently
/// return <c>null</c> for every series lookup.
/// </summary>
public class FredClientSeriesMetadataTests
{
    [Fact]
    public async Task GetSeriesMetadata_KnownSeries_DeserialisesAndQueriesByIdAndApiKey()
    {
        var json =
            "{\"seriess\":[{"
            + "\"id\":\"DGS10\","
            + "\"title\":\"Market Yield on U.S. Treasury Securities at 10-Year Constant Maturity\","
            + "\"frequency\":\"Daily\","
            + "\"frequency_short\":\"D\","
            + "\"units\":\"Percent\","
            + "\"observation_start\":\"1962-01-02\","
            + "\"observation_end\":\"2024-12-31\""
            + "}]}";

        var handler = new CapturingHandler(json);
        var sut = new FredClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FredClient>>(),
            Options.Create(new FredOptions { ApiKey = "secret-key" })
        );

        var record = await sut.GetSeriesMetadata("DGS10");

        record.Should().NotBeNull();
        record.Id.Should().Be("DGS10");
        record.Frequency.Should().Be("Daily");
        record.Units.Should().Be("Percent");

        // URL must carry both series_id and api_key as query parameters — FRED rejects the
        // request otherwise and any test that asserts only on the response would pass even
        // if the URL composition broke.
        handler.LastUrl.Should().Contain("series_id=DGS10");
        handler.LastUrl.Should().Contain("api_key=secret-key");
        handler.LastUrl.Should().Contain("file_type=json");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _body;
        public string LastUrl { get; private set; }

        public CapturingHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastUrl = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
