using System.Net;
using System.Text;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// Sibling to <see cref="FinraClientSettlementDatesAfterTests"/>. Pins
/// <c>GetShortInterestSettlementDatesBetween</c> — the bounded discovery method the scraper
/// uses to backfill settlement dates below the earliest stored one. Critical contract: the
/// window bounds are passed through verbatim (no +1 offset, unlike the "After" variant), the
/// result is HashSet-deduped and sorted ascending, and an inverted window short-circuits
/// without an API call.
/// </summary>
public class FinraClientSettlementDatesBetweenTests
{
    [Fact]
    public async Task GetShortInterestSettlementDatesBetween_FiltersToTheWindow_AndSortsAscending()
    {
        var tokenResponse = "{\"access_token\":\"t\",\"expires_in\":3600}";
        var dataResponse =
            "[{\"settlementDate\":\"2022-06-30\"},{\"settlementDate\":\"2022-06-15\"}]";

        var handler = new CapturingHandler(tokenResponse, dataResponse);
        var sut = new FinraClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FinraClient>>(),
            Options.Create(new FinraOptions { ClientId = "id", ClientSecret = "secret" })
        );

        var dates = await sut.GetShortInterestSettlementDatesBetween(
            new DateOnly(2022, 6, 1),
            new DateOnly(2022, 6, 30)
        );

        dates.Should().Equal(new DateOnly(2022, 6, 15), new DateOnly(2022, 6, 30));
        // The window bounds are passed straight through — no +1 offset (that belongs to the
        // forward-only "After" variant). A wrong offset would silently drop boundary dates.
        handler.LastDataRequestBody.Should().Contain("\"startDate\":\"2022-06-01\"");
        handler.LastDataRequestBody.Should().Contain("\"endDate\":\"2022-06-30\"");
    }

    [Fact]
    public async Task GetShortInterestSettlementDatesBetween_StartAfterEnd_ReturnsEmptyWithoutCallingApi()
    {
        var handler = new CapturingHandler("{\"access_token\":\"t\",\"expires_in\":3600}", "[]");
        var sut = new FinraClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FinraClient>>(),
            Options.Create(new FinraOptions { ClientId = "id", ClientSecret = "secret" })
        );

        // An empty/inverted window (the steady state once backfill has reached the floor)
        // must not waste an API round-trip.
        var dates = await sut.GetShortInterestSettlementDatesBetween(
            new DateOnly(2022, 6, 30),
            new DateOnly(2022, 6, 1)
        );

        dates.Should().BeEmpty();
        handler.LastDataRequestBody.Should().BeNull("an inverted window must not hit the API");
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
