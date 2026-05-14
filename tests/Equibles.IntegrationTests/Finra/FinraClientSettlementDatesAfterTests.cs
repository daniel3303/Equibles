using System.Net;
using System.Text;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// Sibling to <see cref="FinraClientTests"/> and <see cref="FinraClientShortInterestTests"/>.
/// Pins <c>GetShortInterestSettlementDatesAfter</c> — the discovery method used by the
/// scraper worker to find which settlement dates need import. Critical contract: the
/// returned list is the HashSet-deduped + sorted set of <c>settlementDate</c> values,
/// and the outgoing query carries a <c>dateRangeFilters</c> block starting at
/// <c>afterDate + 1 day</c>. A regression that dropped the +1 would re-import the
/// known-latest row every cycle, doubling DB writes.
/// </summary>
public class FinraClientSettlementDatesAfterTests
{
    [Fact]
    public async Task GetShortInterestSettlementDatesAfter_DedupesAndSortsAcrossDuplicates()
    {
        // Three rows, two of which carry the same settlementDate — HashSet must dedup
        // and the final OrderBy must return the dates ascending.
        var tokenResponse = "{\"access_token\":\"t\",\"expires_in\":3600}";
        var dataResponse =
            "["
            + "{\"settlementDate\":\"2024-12-31\"},"
            + "{\"settlementDate\":\"2024-12-15\"},"
            + "{\"settlementDate\":\"2024-12-31\"}"
            + "]";

        var handler = new CapturingHandler(tokenResponse, dataResponse);
        var sut = new FinraClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FinraClient>>(),
            Options.Create(new FinraOptions { ClientId = "id", ClientSecret = "secret" })
        );

        var dates = await sut.GetShortInterestSettlementDatesAfter(new DateOnly(2024, 12, 1));

        dates.Should().Equal(new DateOnly(2024, 12, 15), new DateOnly(2024, 12, 31));

        // The cursor +1 day is load-bearing — without it the next worker tick re-imports
        // the latest known date and FlexLabs upserts would silently no-op on every row.
        handler.LastDataRequestBody.Should().Contain("\"startDate\":\"2024-12-02\"");
        handler.LastDataRequestBody.Should().Contain("\"dateRangeFilters\"");
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
                LastDataRequestBody =
                    request.Content != null
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
