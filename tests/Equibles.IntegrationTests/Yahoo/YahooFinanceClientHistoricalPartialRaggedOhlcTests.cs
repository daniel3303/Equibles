using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

public class YahooFinanceClientHistoricalPartialRaggedOhlcTests
{
    // Contract (oracle): every HistoricalPrice the parser emits must satisfy the
    // OHLC invariants the live smoke test asserts on real Yahoo data —
    // High >= Open, High >= Close, Low <= Close (YahooHistoricalPricesSmokeTests).
    // The sibling ragged-array test pins the "timestamp longer than ALL OHLC
    // arrays" quirk (whole tail dropped because Close is null). This pins the
    // PARTIAL case Yahoo also emits: Close present for a day, but the High/Low/
    // Open column ends one element short. Holiday-gap semantics say an incomplete
    // row is skipped (as the null-Close path does); it must not be emitted as an
    // OHLC-impossible bar (High=0 while Close>0), which would silently import
    // garbage and break the smoke test's own invariants.
    [Fact(Skip = "GH-889 — partial-ragged OHLC emits High=0 bar with Close>0")]
    public async Task GetHistoricalPrices_CloseLongerThanHighColumn_DoesNotEmitOhlcImpossibleBar()
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
        var unixDec24 = new DateTimeOffset(
            2024,
            12,
            24,
            0,
            0,
            0,
            TimeSpan.Zero
        ).ToUnixTimeSeconds();

        // Two timestamps; Close + Volume have both days, but Open/High/Low end
        // after day one — a real Yahoo partial-raggedness quirk.
        var json =
            "{\"chart\":{\"result\":[{\"timestamp\":["
            + unixDec23
            + ","
            + unixDec24
            + "],\"indicators\":{\"quote\":[{"
            + "\"open\":[100.10],\"high\":[101.50],\"low\":[99.80],"
            + "\"close\":[101.00,102.00],\"volume\":[1500000,1600000]}]}}]}}";

        var sut = new YahooFinanceClient(
            new HttpClient(new StubHandler(json)),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var prices = await sut.GetHistoricalPrices(
            "AAPL",
            new DateOnly(2024, 12, 23),
            new DateOnly(2024, 12, 24)
        );

        prices.Should().OnlyContain(p => p.High >= p.Open && p.High >= p.Close && p.Low <= p.Close);
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
