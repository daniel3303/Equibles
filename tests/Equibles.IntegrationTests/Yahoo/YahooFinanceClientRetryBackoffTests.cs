using System.Net;
using System.Reflection;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// The other YahooFinanceClient tests only drive the happy path, so
/// <c>SendWithRetry</c>'s transient-failure arms are uncovered. This pins the
/// 429 and 5xx branches: a rate-limit / server-error response must trigger an
/// exponential-backoff retry and the subsequent success must be returned —
/// not surfaced as an exception.
///
/// The session crumb/cookie is cached in private statics that <c>EnsureSession</c>
/// would otherwise acquire over a non-injectable HttpClient (real network). We
/// snapshot those statics, seed a valid in-memory session so the retry loop runs
/// network-free, and restore them in a finally so the real session other Yahoo
/// tests may rely on is left untouched.
/// </summary>
public class YahooFinanceClientRetryBackoffTests
{
    private static readonly FieldInfo CrumbField = typeof(YahooFinanceClient).GetField(
        "_cachedCrumb",
        BindingFlags.NonPublic | BindingFlags.Static
    );
    private static readonly FieldInfo CookieField = typeof(YahooFinanceClient).GetField(
        "_cachedCookieHeader",
        BindingFlags.NonPublic | BindingFlags.Static
    );
    private static readonly FieldInfo ExpiryField = typeof(YahooFinanceClient).GetField(
        "_sessionExpiry",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static async Task WithSeededSession(Func<Task> body)
    {
        var prevCrumb = CrumbField.GetValue(null);
        var prevCookie = CookieField.GetValue(null);
        var prevExpiry = ExpiryField.GetValue(null);
        try
        {
            CrumbField.SetValue(null, "test-crumb");
            CookieField.SetValue(null, "A=1");
            ExpiryField.SetValue(null, DateTime.UtcNow.AddMinutes(30));
            await body();
        }
        finally
        {
            CrumbField.SetValue(null, prevCrumb);
            CookieField.SetValue(null, prevCookie);
            ExpiryField.SetValue(null, prevExpiry);
        }
    }

    [Fact]
    public async Task GetHistoricalPrices_RateLimitedThenOk_RetriesWithBackoffAndReturns()
    {
        await WithSeededSession(async () =>
        {
            var handler = new SequenceHandler(HttpStatusCode.TooManyRequests);
            var sut = new YahooFinanceClient(
                new HttpClient(handler),
                Substitute.For<ILogger<YahooFinanceClient>>()
            );

            var prices = await sut.GetHistoricalPrices(
                "AAPL",
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 1, 2)
            );

            // Empty chart payload → no rows; the point is the call SUCCEEDED
            // after a retry rather than throwing the max-retries exception.
            prices.Should().BeEmpty();
            handler.CallCount.Should().Be(2, "first 429 must be retried, second 200 returned");
        });
    }

    [Fact]
    public async Task GetHistoricalPrices_ServerErrorThenOk_RetriesWithBackoffAndReturns()
    {
        await WithSeededSession(async () =>
        {
            var handler = new SequenceHandler(HttpStatusCode.InternalServerError);
            var sut = new YahooFinanceClient(
                new HttpClient(handler),
                Substitute.For<ILogger<YahooFinanceClient>>()
            );

            var prices = await sut.GetHistoricalPrices(
                "MSFT",
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 1, 2)
            );

            prices.Should().BeEmpty();
            handler.CallCount.Should().Be(2, "first 5xx must be retried, second 200 returned");
        });
    }

    // First call returns the supplied transient status, every later call 200
    // with an empty (but JSON-valid) chart body.
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
                return Task.FromResult(new HttpResponseMessage(_firstStatus));
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
