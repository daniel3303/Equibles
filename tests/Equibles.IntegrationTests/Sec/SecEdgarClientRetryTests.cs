using System.Net;
using System.Net.Http.Headers;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The existing SecEdgarClient pins cover the two GetCompanyMetadata catches and
/// the malformed-JSON path, but never a transient HTTP failure — so
/// <c>SendWithRetryAsync</c>'s 429 and 5xx retry arms were uncovered. This pins
/// them through <c>GetCompanyMetadata</c>: the first response is the transient
/// status carrying <c>Retry-After: 0</c> (so <c>GetRetryDelay</c> returns zero
/// and the backoff is instant), the retried request succeeds, and metadata is
/// returned rather than the failure propagating.
/// </summary>
public class SecEdgarClientRetryTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetCompanyMetadata_TransientThenOk_RetriesAndReturnsMetadata(
        HttpStatusCode transientStatus
    )
    {
        var handler = new SequenceHandler(transientStatus);
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

        var metadata = await sut.GetCompanyMetadata("1234567");

        metadata.Should().NotBeNull("the retried request returned a 200 body");
        handler.CallCount.Should().Be(2, "the transient failure must be retried once");
    }

    // First call: the transient status with Retry-After: 0 (instant backoff).
    // Every later call: 200 with a minimal valid SEC submissions JSON body.
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _firstStatus;
        public int CallCount { get; private set; }

        public SequenceHandler(HttpStatusCode firstStatus) => _firstStatus = firstStatus;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            if (CallCount == 1)
            {
                var transient = new HttpResponseMessage(_firstStatus);
                transient.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(transient);
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }
            );
        }
    }
}
