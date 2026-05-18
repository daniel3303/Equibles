using System.Net;
using System.Reflection;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Regression for GH-904. Yahoo stamps daily-bar timestamps in the exchange's
/// local time; the request window and the returned <c>Date</c> must use that
/// exchange-local calendar (via <c>meta.gmtoffset</c>), not UTC midnight. A
/// Tokyo (UTC+9) bar for 2024-01-31 is stamped 2024-01-30T15:00Z — under the
/// old UTC-naive logic it was mis-dated to 2024-01-30 and fell outside a
/// 2024-01-31 window. Seeded-session reflection keeps the retry loop
/// network-free; the cached session fields are restored in a finally.
/// </summary>
public class YahooFinanceClientHistoricalExchangeLocalDateTests
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
    public async Task GetHistoricalPrices_EastOfUtcExchange_DatesBarOnExchangeLocalDay()
    {
        await WithSeededSession(async () =>
        {
            // 2024-01-31 00:00 in Tokyo (UTC+9) == 2024-01-30 15:00 UTC.
            var tokyoBarUtc = new DateTimeOffset(
                2024,
                1,
                30,
                15,
                0,
                0,
                TimeSpan.Zero
            ).ToUnixTimeSeconds();

            var json =
                "{\"chart\":{\"result\":[{"
                + "\"meta\":{\"gmtoffset\":32400,\"exchangeTimezoneName\":\"Asia/Tokyo\"},"
                + "\"timestamp\":["
                + tokyoBarUtc
                + "],\"indicators\":{\"quote\":[{"
                + "\"open\":[100.10],\"high\":[101.50],\"low\":[99.80],"
                + "\"close\":[101.00],\"volume\":[1500000]}],"
                + "\"adjclose\":[{\"adjclose\":[100.90]}]}}]}}";

            var sut = new YahooFinanceClient(
                new HttpClient(new StubHandler(json)),
                Substitute.For<ILogger<YahooFinanceClient>>()
            );

            var prices = await sut.GetHistoricalPrices(
                "7203.T",
                new DateOnly(2024, 1, 31),
                new DateOnly(2024, 1, 31)
            );

            // The bar belongs to the Tokyo trading day 2024-01-31, not the UTC
            // date 2024-01-30 — and must be inside the requested window.
            prices.Should().ContainSingle();
            prices[0].Date.Should().Be(new DateOnly(2024, 1, 31));
        });
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
