using System.Net;
using System.Text;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// Adversarial sibling to <see cref="FinraClientSettlementDatesAllTests"/>,
/// which only ever returns one short page. The contract is "extract unique
/// dates across pages": when page 1 fills the 5000-row page limit the client
/// MUST keep paging and fold later pages in. A broken offset/continuation
/// would silently truncate the initial backfill to whatever fits in page 1.
/// </summary>
public class FinraClientSettlementDatesPaginationTests
{
    [Fact]
    public async Task GetShortInterestSettlementDates_FullFirstPage_ContinuesPagingAndDedupesAcrossPages()
    {
        const int pageSize = 5000;
        var tokenResponse = "{\"access_token\":\"t\",\"expires_in\":3600}";

        // Page 1: a full page (== limit) of the SAME date — pins both
        // continuation (count == limit ⇒ keep going) and cross-page dedup.
        var page1 = new StringBuilder("[");
        for (var i = 0; i < pageSize; i++)
        {
            if (i > 0)
                page1.Append(',');
            page1.Append("{\"settlementDate\":\"2024-11-15\"}");
        }
        page1.Append(']');

        // Page 2: a single new date, shorter than the limit ⇒ loop stops here.
        const string page2 = "[{\"settlementDate\":\"2024-12-31\"}]";

        var handler = new PagingHandler(tokenResponse, [page1.ToString(), page2]);
        var sut = new FinraClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FinraClient>>(),
            Options.Create(new FinraOptions { ClientId = "id", ClientSecret = "secret" })
        );

        var dates = await sut.GetShortInterestSettlementDates();

        dates.Should().Equal(new DateOnly(2024, 11, 15), new DateOnly(2024, 12, 31));
        handler.DataRequestCount.Should().Be(2, "a full first page must trigger a second fetch");
    }

    private sealed class PagingHandler : HttpMessageHandler
    {
        private readonly string _tokenBody;
        private readonly string[] _pages;
        public int DataRequestCount { get; private set; }

        public PagingHandler(string tokenBody, string[] pages)
        {
            _tokenBody = tokenBody;
            _pages = pages;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string body;
            if (request.RequestUri!.AbsoluteUri.Contains("oauth2/access_token"))
            {
                body = _tokenBody;
            }
            else
            {
                var index = DataRequestCount;
                DataRequestCount++;
                body = index < _pages.Length ? _pages[index] : "[]";
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
