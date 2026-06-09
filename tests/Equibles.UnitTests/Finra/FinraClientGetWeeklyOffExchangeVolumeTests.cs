using System.Net;
using System.Reflection;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Record-replay (cassette) pin for GetWeeklyOffExchangeVolume. A captured FINRA
/// weeklySummary payload with one ATS_W_SMBL row and one OTC_W_SMBL row for a single
/// symbol is replayed through the real client and HTTP stack offline. The client must
/// deserialize every field exactly as FINRA's camelCase wire keys map to the DTO —
/// issueSymbolIdentifier → Symbol, weekStartDate, summaryTypeCode, the two Number
/// quantities, and tierIdentifier. A wrong JsonProperty name silently nulls/zeros the
/// field, so every value is asserted exactly against the frozen cassette.
/// </summary>
public class FinraClientGetWeeklyOffExchangeVolumeTests
{
    private const string WeeklySummaryPayload = """
        [
            {
                "issueSymbolIdentifier": "AAPL",
                "weekStartDate": "2024-03-04",
                "summaryTypeCode": "ATS_W_SMBL",
                "totalWeeklyShareQuantity": 1234567,
                "totalWeeklyTradeCount": 8901,
                "tierIdentifier": "T1"
            },
            {
                "issueSymbolIdentifier": "AAPL",
                "weekStartDate": "2024-03-04",
                "summaryTypeCode": "OTC_W_SMBL",
                "totalWeeklyShareQuantity": 7654321,
                "totalWeeklyTradeCount": 2109,
                "tierIdentifier": "T1"
            }
        ]
        """;

    [Fact]
    public async Task GetWeeklyOffExchangeVolume_ParsesAtsAndNonAtsRows_WithExactFieldValues()
    {
        // FinraClient caches the OAuth token in a private static field shared by every
        // instance. Sibling tests (e.g. the Hijri-culture pin) assume a cold cache so their
        // capture handler's first call is the token request. Clear the static cache before
        // and after so this test neither inherits a stale token nor leaks one to others.
        ResetTokenCache();

        var handler = new ReplayHandler(WeeklySummaryPayload);
        var options = Options.Create(new FinraOptions { ClientId = "test", ClientSecret = "test" });
        var sut = new FinraClient(
            new HttpClient(handler),
            NullLogger<FinraClient>.Instance,
            options
        );

        var records = await sut.GetWeeklyOffExchangeVolume(new DateOnly(2024, 3, 4));

        ResetTokenCache();

        records.Should().HaveCount(2);

        var ats = records.Single(r => r.SummaryTypeCode == "ATS_W_SMBL");
        ats.Symbol.Should().Be("AAPL");
        ats.WeekStartDate.Should().Be("2024-03-04");
        ats.TotalWeeklyShareQuantity.Should().Be(1_234_567);
        ats.TotalWeeklyTradeCount.Should().Be(8_901);
        ats.TierIdentifier.Should().Be("T1");

        var nonAts = records.Single(r => r.SummaryTypeCode == "OTC_W_SMBL");
        nonAts.Symbol.Should().Be("AAPL");
        nonAts.TotalWeeklyShareQuantity.Should().Be(7_654_321);
        nonAts.TotalWeeklyTradeCount.Should().Be(2_109);
    }

    private static void ResetTokenCache()
    {
        typeof(FinraClient)
            .GetField("_cachedToken", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, null);
    }

    // Returns the OAuth token for the token endpoint and replays the frozen weeklySummary
    // payload for every data request. The token is cached statically across the client's
    // lifetime, so a prior test may have already obtained one — dispatch on the request URL
    // rather than a call counter so this handler works whether or not the token call fires.
    private sealed class ReplayHandler : HttpMessageHandler
    {
        private readonly string _payload;

        public ReplayHandler(string payload) => _payload = payload;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri!.AbsoluteUri.Contains("oauth2/access_token"))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"access_token\":\"test\",\"expires_in\":3600}"
                        ),
                    }
                );
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_payload) }
            );
        }
    }
}
