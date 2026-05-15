using System.Net;
using System.Text;
using Equibles.Integrations.Fred;
using Equibles.Integrations.Fred.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// Sibling to <see cref="FredClientSeriesMetadataTests"/>, which pins the happy
/// metadata fetch. Every existing FRED test only ever serves 200s, so the
/// transient-failure resilience in <c>SendWithRetry</c> is uncovered. This pins
/// the <c>(int)response.StatusCode >= 500</c> retry branch: a 503 on the first
/// attempt must be retried, not surfaced. FRED returns sporadic 5xx under load;
/// a regression dropping that branch would fail every economic-indicator sync
/// on the first hiccup instead of recovering on the next attempt.
/// </summary>
public class FredClientSeriesMetadataRetryTests
{
    [Fact]
    public async Task GetSeriesMetadata_ServerErrorThenSuccess_RetriesAndReturnsRecord()
    {
        var json =
            "{\"seriess\":[{\"id\":\"DGS10\",\"title\":\"10-Year Treasury\","
            + "\"frequency\":\"Daily\",\"units\":\"Percent\"}]}";

        var handler = new FlakyHandler(json);
        var sut = new FredClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FredClient>>(),
            Options.Create(new FredOptions { ApiKey = "secret-key" })
        );

        var record = await sut.GetSeriesMetadata("DGS10");

        // First attempt 503 -> retried; second attempt 200 -> parsed.
        handler.Attempts.Should().Be(2);
        record.Should().NotBeNull();
        record.Id.Should().Be("DGS10");
    }

    private sealed class FlakyHandler : HttpMessageHandler
    {
        private readonly string _body;
        public int Attempts { get; private set; }

        public FlakyHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Attempts++;
            if (Attempts == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
