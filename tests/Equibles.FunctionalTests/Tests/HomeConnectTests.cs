using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HomeConnectTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HomeConnectTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Connect_GetWithoutApiKey_RendersEndpointAndOpenServerWarning() {
        // The page composes the MCP endpoint from Configuration["McpPort"] (defaulting to 8081)
        // + the live request host, then branches the body on whether McpApiKey is configured.
        // The fixture sets no McpApiKey, so the un-secured branch must render — that is the
        // explicit "open to everyone" warning copy. A regression that swaps the conditional or
        // drops the alert would silently mask a security signal users rely on.
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/home/connect");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Connect Your AI Assistant");

        // The default port (8081) composed into a /mcp URL — host varies (127.0.0.1 vs localhost),
        // so anchor on the suffix the controller controls.
        await Assertions.Expect(page.Locator("code").First).ToContainTextAsync(":8081/mcp");

        await Assertions.Expect(page.Locator("text=MCP server is open to everyone")).ToBeVisibleAsync();
    }
}
