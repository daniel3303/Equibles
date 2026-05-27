using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Sibling to YahooFinanceClientCompanyProfileTests (which pins the populated
/// happy path). The `if (profile == null) return null;` guard fires when Yahoo
/// returns a quoteSummary result that lacks the `assetProfile` module — common
/// for ETFs, ADRs, and tickers whose profile metadata hasn't been backfilled.
/// A refactor that dropped the null guard (or replaced it with a `new
/// CompanyProfile()` default fallback) would persist an empty Sector/Industry
/// row, overwriting any prior value the StockSyncService had merged in from a
/// previous successful call. Pin the null return so the caller's
/// "skip-on-null" branch keeps the existing record intact.
/// </summary>
public class YahooFinanceClientCompanyProfileMissingModuleTests
{
    [Fact]
    public async Task GetCompanyProfile_ResultPresentButAssetProfileModuleMissing_ReturnsNull()
    {
        // Result object is present but carries no assetProfile module — a real
        // Yahoo response shape for tickers without analyst-curated profile data.
        var json = "{\"quoteSummary\":{\"result\":[{}]}}";

        var sut = new YahooFinanceClient(
            new HttpClient(new ConstantHandler(json)),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var profile = await sut.GetCompanyProfile("ETFTKR");

        profile.Should().BeNull();
    }

    private sealed class ConstantHandler : HttpMessageHandler
    {
        private readonly string _body;

        public ConstantHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
    }
}
