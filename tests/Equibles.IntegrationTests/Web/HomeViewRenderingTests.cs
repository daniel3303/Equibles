using System.Net;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// First in-process view-rendering coverage: the Home controller's views need
/// no seed data and no auth, so they pin the Razor pipeline end-to-end (routing
/// → controller → compiled view + shared layout + the FlashMessages partial that
/// renders on every page) without database setup. Every assertion targets
/// rendered HTML the view is responsible for emitting, so a broken view template
/// fails the test rather than silently returning an empty body.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HomeViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public HomeViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetIndex_RendersHomeViewWithLayout()
    {
        var response = await _fixture.Client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Equibles", "the home view sets and renders its title");
        html.Should().Contain("<html", "the shared layout must wrap the view");
    }

    [Fact]
    public async Task GetConnect_RendersConnectViewWithMcpUrl()
    {
        var response = await _fixture.Client.GetAsync("/Home/Connect");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        // Connect() computes an MCP URL from the request scheme/host and pushes it
        // into ViewData — the view must render it, exercising the data-driven path.
        html.Should().Contain("/mcp", "the connect view renders the computed MCP URL");
    }

    [Fact]
    public async Task GetError404_RendersErrorViewWithNotFoundStatus()
    {
        var response = await _fixture.Client.GetAsync("/Home/Error/404");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Page Not Found", "the error view renders the 404 title");
    }
}
