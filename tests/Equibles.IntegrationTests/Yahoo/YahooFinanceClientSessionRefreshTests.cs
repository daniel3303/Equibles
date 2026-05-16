using System.Net;
using System.Reflection;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Pins <c>SendWithRetry</c>'s auth-failure (401/403) session-refresh arm and
/// the max-retries-exceeded throw — previously unreachable because the live
/// EnsureSession bootstrap uses a self-managed HttpClient. A test subclass
/// overrides the new <c>EnsureSession</c> seam to supply a canned session, so
/// the retry loop runs network-free. The shared session statics are
/// snapshotted/restored (InvalidateSession mutates them).
/// </summary>
public class YahooFinanceClientSessionRefreshTests
{
    private sealed class StubbedSessionClient : YahooFinanceClient
    {
        public StubbedSessionClient(HttpClient httpClient, ILogger<YahooFinanceClient> logger)
            : base(httpClient, logger) { }

        protected override Task<(string Crumb, string CookieHeader)> EnsureSession() =>
            Task.FromResult(("test-crumb", "A=1"));
    }

    private static async Task WithRestoredStatics(Func<Task> body)
    {
        var t = typeof(YahooFinanceClient);
        var fc = t.GetField("_cachedCrumb", BindingFlags.NonPublic | BindingFlags.Static);
        var fk = t.GetField("_cachedCookieHeader", BindingFlags.NonPublic | BindingFlags.Static);
        var fe = t.GetField("_sessionExpiry", BindingFlags.NonPublic | BindingFlags.Static);
        var pc = fc.GetValue(null);
        var pk = fk.GetValue(null);
        var pe = fe.GetValue(null);
        try
        {
            await body();
        }
        finally
        {
            fc.SetValue(null, pc);
            fk.SetValue(null, pk);
            fe.SetValue(null, pe);
        }
    }

    [Fact]
    public async Task SendWithRetry_AuthFailsThenSucceeds_RefreshesSessionAndReturns()
    {
        await WithRestoredStatics(async () =>
        {
            var handler = new SequenceHandler(HttpStatusCode.Unauthorized);
            var sut = new StubbedSessionClient(
                new HttpClient(handler),
                Substitute.For<ILogger<YahooFinanceClient>>()
            );

            var prices = await sut.GetHistoricalPrices(
                "AAPL",
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 1, 2)
            );

            prices.Should().BeEmpty("the retried request returned an empty chart body");
            handler.CallCount.Should().Be(2, "the 401 must be retried after a session refresh");
        });
    }

    [Fact]
    public async Task SendWithRetry_AuthFailsEveryAttempt_ThrowsMaxRetriesExceeded()
    {
        await WithRestoredStatics(async () =>
        {
            var handler = new AlwaysHandler(HttpStatusCode.Forbidden);
            var sut = new StubbedSessionClient(
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
        });
    }

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

    private sealed class AlwaysHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public AlwaysHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(_status));
    }
}
