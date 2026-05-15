using System.Net;
using System.Text;
using Equibles.Integrations.Fred;
using Equibles.Integrations.Fred.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// Sibling to <see cref="FredClientSeriesMetadataRetryTests"/>, which pins the
/// 5xx retry branch. This pins the 429 branch of SendWithRetry (lines 118-128,
/// zero-hit): FRED enforces a hard 120 req/min limit and returns 429 when
/// exceeded — that must be retried, not surfaced. A regression dropping the
/// branch would fail every economic-indicator sync the moment FRED throttles.
/// </summary>
public class FredClientRateLimitRetryTests
{
    [Fact]
    public async Task GetObservations_RateLimitedThenSuccess_RetriesAndReturnsObservations()
    {
        var json =
            "{\"count\":1,\"offset\":0,\"limit\":100000,"
            + "\"observations\":[{\"date\":\"2024-01-01\",\"value\":\"3.5\"}]}";

        var handler = new RateLimitedThenOkHandler(json);
        var sut = new FredClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FredClient>>(),
            Options.Create(new FredOptions { ApiKey = "secret-key" })
        );

        var observations = await sut.GetObservations("DGS10");

        // First attempt 429 -> retried; second attempt 200 -> parsed.
        handler.Attempts.Should().Be(2);
        observations.Should().ContainSingle();
        observations[0].Value.Should().Be("3.5");
    }

    private sealed class RateLimitedThenOkHandler : HttpMessageHandler
    {
        private readonly string _json;
        public int Attempts { get; private set; }

        public RateLimitedThenOkHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Attempts++;
            if (Attempts == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
