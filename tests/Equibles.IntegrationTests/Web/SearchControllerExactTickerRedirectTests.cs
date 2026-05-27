using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Covers the exact-ticker shortcut on the global search: submitting a query that
/// is an exact ticker (case-insensitive, primary or secondary) on the unfiltered
/// overview redirects straight to the stock page instead of rendering results. A
/// company-name / partial query, or a query with a category filter applied, still
/// renders the results page.
/// </summary>
[Collection(WebHostCollection.Name)]
public class SearchControllerExactTickerRedirectTests
{
    private readonly WebHostFixture _fixture;

    public SearchControllerExactTickerRedirectTests(WebHostFixture fixture) => _fixture = fixture;

    private async Task SeedStock()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Ticker = "GXA",
                    Name = "Globex Alpha Corp",
                    SecondaryTickers = ["GXA.B"],
                    MarketCapitalization = 1_000_000_000d,
                }
            );
            await Task.CompletedTask;
        });
    }

    // Dedicated non-redirecting client so the 302 itself is observable (the shared
    // fixture client auto-follows redirects).
    private HttpClient CreateNonRedirectingClient() =>
        new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = _fixture.Client.BaseAddress,
        };

    [Fact]
    public async Task Index_ExactTicker_RedirectsToStockPage()
    {
        await SeedStock();
        using var client = CreateNonRedirectingClient();

        var response = await client.GetAsync("/Search?q=GXA");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.ToString().ToLowerInvariant().Should().Contain("/stocks/gxa");
    }

    [Fact]
    public async Task Index_ExactTickerLowercase_RedirectsToStockPage()
    {
        await SeedStock();
        using var client = CreateNonRedirectingClient();

        var response = await client.GetAsync("/Search?q=gxa");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.ToString().ToLowerInvariant().Should().Contain("/stocks/gxa");
    }

    [Fact]
    public async Task Index_ExactSecondaryTicker_RedirectsToPrimaryStockPage()
    {
        await SeedStock();
        using var client = CreateNonRedirectingClient();

        var response = await client.GetAsync("/Search?q=GXA.B");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.ToString().ToLowerInvariant().Should().Contain("/stocks/gxa");
    }

    [Fact]
    public async Task Index_CompanyNameQuery_RendersSearchPage()
    {
        await SeedStock();
        using var client = CreateNonRedirectingClient();

        var response = await client.GetAsync("/Search?q=Globex");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Globex Alpha Corp");
    }

    [Fact]
    public async Task Index_ExactTickerWithCategoryFilter_DoesNotRedirect()
    {
        await SeedStock();
        using var client = CreateNonRedirectingClient();

        var response = await client.GetAsync("/Search?q=GXA&category=Stocks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
