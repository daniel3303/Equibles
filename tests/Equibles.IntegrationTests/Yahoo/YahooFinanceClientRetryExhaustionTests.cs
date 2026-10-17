using System.Net;
using Equibles.Integrations.Yahoo;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// <see cref="YahooFinanceClientRetryBackoffTests"/> pins only the
/// transient-then-OK arms of <c>SendWithRetry</c>. The terminal arm — a
/// PERSISTENT 5xx that never recovers — is uncovered. Contract (from the
/// method name, the <c>MaxRetries = 3</c> constant, and the explicit
/// <c>throw new HttpRequestException("Max retries exceeded...")</c>): the loop
/// must give up after MaxRetries retries and surface an
/// <see cref="HttpRequestException"/> — never spin forever and never return a
/// bogus success. With 1 initial attempt + 3 retries that is exactly 4 HTTP
/// hops.
/// </summary>
public class YahooFinanceClientRetryExhaustionTests
{
    [Fact]
    public async Task GetHistoricalPrices_PersistentServerError_ThrowsAfterMaxRetries()
    {
        var handler = new AlwaysFailHandler(HttpStatusCode.InternalServerError);
        var sut = new OfflineYahooFinanceClient(
            new HttpClient(handler),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var act = async () =>
            await sut.GetHistoricalPrices(
                "AAPL",
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 1, 2)
            );

        await act.Should().ThrowAsync<HttpRequestException>();
        // MaxRetries = 3 → 1 initial attempt + 3 retries. Never an infinite
        // loop, never a swallowed success.
        handler.CallCount.Should().Be(4);
    }

    private sealed class AlwaysFailHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public int CallCount { get; private set; }

        public AlwaysFailHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }
}
