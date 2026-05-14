using System.Net;
using System.Text;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// Sibling to <see cref="FinraClientSettlementDatesAfterTests"/>. That pins the
/// after-cursor variant; this pins the unfiltered <c>GetShortInterestSettlementDates</c>
/// — the one called on first-ever run (no rows in DB → cursor unknown). The outgoing
/// request must NOT carry <c>dateRangeFilters</c> — adding one would silently scope
/// the discovery query, returning only the last few dates and breaking the
/// initial-backfill seed.
/// </summary>
public class FinraClientSettlementDatesAllTests
{
    [Fact]
    public async Task GetShortInterestSettlementDates_NoCursor_OmitsDateRangeFiltersAndReturnsSortedAscending()
    {
        var tokenResponse = "{\"access_token\":\"t\",\"expires_in\":3600}";
        // Out-of-order with a duplicate to pin (a) sort-ascending and (b) HashSet dedup.
        var dataResponse =
            "[{\"settlementDate\":\"2024-11-15\"},"
            + "{\"settlementDate\":\"2024-10-31\"},"
            + "{\"settlementDate\":\"2024-11-15\"}]";

        var handler = new CapturingHandler(tokenResponse, dataResponse);
        var sut = new FinraClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FinraClient>>(),
            Options.Create(new FinraOptions { ClientId = "id", ClientSecret = "secret" })
        );

        var dates = await sut.GetShortInterestSettlementDates();

        dates.Should().Equal(new DateOnly(2024, 10, 31), new DateOnly(2024, 11, 15));
        handler.LastDataRequestBody.Should().NotContain("dateRangeFilters");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _tokenBody;
        private readonly string _dataBody;
        public string LastDataRequestBody { get; private set; }

        public CapturingHandler(string tokenBody, string dataBody)
        {
            _tokenBody = tokenBody;
            _dataBody = dataBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var isToken = request.RequestUri!.AbsoluteUri.Contains("oauth2/access_token");
            string body;
            if (isToken)
            {
                body = _tokenBody;
            }
            else
            {
                LastDataRequestBody = request.Content != null
                    ? await request.Content.ReadAsStringAsync(cancellationToken)
                    : "";
                body = _dataBody;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
