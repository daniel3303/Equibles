using System.Net;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the shared layout's top navbar shape: the Institutions link is present
/// between Stocks and the More dropdown, and the dropdown carries Economic Data
/// / Futures / Market. Each fact hits the homepage (any page renders the same
/// layout) and asserts on the response HTML — cheap and stable.
/// </summary>
[Collection(WebHostCollection.Name)]
public class LayoutNavbarTests
{
    private readonly WebHostFixture _fixture;

    public LayoutNavbarTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetHome_DesktopNav_ContainsInstitutionsLinkBetweenStocksAndMoreDropdown()
    {
        var response = await _fixture.Client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        var stocksIdx = html.IndexOf("href=\"/stocks\"", StringComparison.Ordinal);
        var institutionsIdx = html.IndexOf("href=\"/institutions\"", StringComparison.Ordinal);
        var moreIdx = html.IndexOf("navbar-more-menu", StringComparison.Ordinal);

        stocksIdx.Should().BeGreaterThan(-1);
        institutionsIdx
            .Should()
            .BeGreaterThan(stocksIdx, "Institutions sits between Stocks and More");
        moreIdx.Should().BeGreaterThan(institutionsIdx, "More dropdown follows Institutions");
    }

    [Fact]
    public async Task GetHome_MoreDropdown_GroupsEconomicDataFuturesAndMarket()
    {
        var response = await _fixture.Client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        // Slice the rendered HTML around the More dropdown's <ul> so the three
        // links are matched inside it rather than coincidentally elsewhere on
        // the page.
        var ulStart = html.IndexOf("id=\"navbar-more-menu\"", StringComparison.Ordinal);
        ulStart.Should().BeGreaterThan(-1);
        var ulEnd = html.IndexOf("</ul>", ulStart, StringComparison.Ordinal);
        ulEnd.Should().BeGreaterThan(ulStart);
        var dropdown = html.Substring(ulStart, ulEnd - ulStart);

        dropdown.Should().Contain("Economic Data");
        dropdown.Should().Contain("Futures");
        dropdown.Should().Contain("Market");
    }

    [Fact]
    public async Task GetHome_MobileNav_ContainsInstitutionsLinkFlat()
    {
        var response = await _fixture.Client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        // The mobile dropdown keeps a flat list — Institutions should appear
        // alongside the rest, not nested behind a sub-dropdown.
        var mobileStart = html.IndexOf("id=\"mobile-nav-menu\"", StringComparison.Ordinal);
        mobileStart.Should().BeGreaterThan(-1);
        var mobileEnd = html.IndexOf("</ul>", mobileStart, StringComparison.Ordinal);
        mobileEnd.Should().BeGreaterThan(mobileStart);
        var mobileMenu = html.Substring(mobileStart, mobileEnd - mobileStart);

        mobileMenu.Should().Contain("Institutions");
        mobileMenu.Should().Contain("Economic Data");
        mobileMenu.Should().Contain("Futures");
        mobileMenu.Should().Contain("Market");
    }
}
