using System.Net;
using System.Reflection;
using Equibles.Integrations.Yahoo;
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
/// hops. The seeded-session reflection mirrors the sibling so the retry loop
/// runs network-free; the cached session fields are restored in a finally.
/// </summary>
public class YahooFinanceClientRetryExhaustionTests
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
    public async Task GetHistoricalPrices_PersistentServerError_ThrowsAfterMaxRetries()
    {
        await WithSeededSession(async () =>
        {
            var handler = new AlwaysFailHandler(HttpStatusCode.InternalServerError);
            var sut = new YahooFinanceClient(
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
        });
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
