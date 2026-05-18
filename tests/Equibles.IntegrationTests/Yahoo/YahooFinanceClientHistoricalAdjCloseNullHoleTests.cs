using System.Net;
using System.Reflection;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Contract (oracle) — from the live smoke test's invariant
/// <c>p.AdjustedClose &gt; 0</c> and the documented fallback "AdjustedClose
/// falls back to the day's Close when adjclose is unavailable": for a row
/// whose Close is a valid positive number, AdjustedClose must equal Close
/// whenever the upstream adjclose value is absent. Distinct from GH-889
/// (OHLC High/Low/Open) — this is the <c>adjclose</c> column carrying a
/// null HOLE at a populated row (a real Yahoo quirk near splits/holiday
/// edges) while the array itself is present and full-length, so the
/// "array null/short → fall back to Close" path is NOT taken. Seeded-session
/// reflection keeps the retry loop network-free; fields restored in finally.
/// </summary>
public class YahooFinanceClientHistoricalAdjCloseNullHoleTests
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
    public async Task GetHistoricalPrices_AdjCloseArrayPresentWithNullHole_FallsBackToClose()
    {
        await WithSeededSession(async () =>
        {
            var unixDec23 = new DateTimeOffset(
                2024,
                12,
                23,
                0,
                0,
                0,
                TimeSpan.Zero
            ).ToUnixTimeSeconds();

            // adjclose array IS present and full-length, but the single row's
            // value is null (Yahoo quirk). Close is a valid positive number.
            var json =
                "{\"chart\":{\"result\":[{\"timestamp\":["
                + unixDec23
                + "],\"indicators\":{\"quote\":[{"
                + "\"open\":[100.10],\"high\":[101.50],\"low\":[99.80],"
                + "\"close\":[101.00],\"volume\":[1500000]}],"
                + "\"adjclose\":[{\"adjclose\":[null]}]}}]}}";

            var sut = new YahooFinanceClient(
                new HttpClient(new StubHandler(json)),
                Substitute.For<ILogger<YahooFinanceClient>>()
            );

            var prices = await sut.GetHistoricalPrices(
                "AAPL",
                new DateOnly(2024, 12, 23),
                new DateOnly(2024, 12, 23)
            );

            prices.Should().ContainSingle();
            // Documented fallback: missing adjclose → use the day's Close,
            // never 0 (which would violate the smoke test's AdjustedClose > 0).
            prices[0].AdjustedClose.Should().Be(prices[0].Close);
            prices[0].AdjustedClose.Should().BeGreaterThan(0);
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
