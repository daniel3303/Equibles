using System.Net;
using System.Reflection;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// <see cref="HouseDisclosureClientIndexNotFoundTests"/> pins the 404 short-circuit;
/// the unit tier covers only parsing. <c>SendWithRetryAsync</c>'s transient-failure
/// arms — exponential-backoff retry on 429 and 5xx — were uncovered. This invokes
/// the private retry helper directly through a sequenced stub transport: the first
/// response is the transient failure, the retry must succeed and the success
/// response be returned rather than surfaced as the max-retries exception.
/// </summary>
public class HouseDisclosureClientRetryTests
{
    private static Task<HttpResponseMessage> InvokeSendWithRetry(
        HouseDisclosureClient client,
        string url
    )
    {
        var method = typeof(HouseDisclosureClient).GetMethod(
            "SendWithRetryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        return (Task<HttpResponseMessage>)method.Invoke(client, [url, CancellationToken.None]);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SendWithRetryAsync_TransientThenOk_RetriesWithBackoffAndReturnsOk(
        HttpStatusCode transientStatus
    )
    {
        var handler = new SequenceHandler(transientStatus);
        var client = new HouseDisclosureClient(
            new HttpClient(handler),
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        using var response = await InvokeSendWithRetry(client, "https://example.test/doc.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.CallCount.Should().Be(2, "the transient failure must be retried once");
    }

    // First call returns the supplied transient status; every later call 200.
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
            return Task.FromResult(
                new HttpResponseMessage(CallCount == 1 ? _firstStatus : HttpStatusCode.OK)
            );
        }
    }
}
