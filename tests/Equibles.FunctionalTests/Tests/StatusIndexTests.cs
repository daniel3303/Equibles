using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StatusIndexTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StatusIndexTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_GetWithEmptyData_RendersSystemStatusHeaderAndAutoRefreshControls() {
        // StatusController.Index composes worker statuses, MCP key state, and the recent-errors
        // list. With no workers registered and no errors in the database the page must still
        // render the header and the auto-refresh control surface — both are static UI scaffolding
        // that depend on the view model being non-null. A regression that throws on Workers.Count
        // or RecentErrors enumeration (because some sub-collection was null instead of empty)
        // would surface here, not in any unit test.
        await _web.ResetAndSeedAsync();   // guarantee empty DB regardless of test ordering
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/status");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("System Status");
        await Assertions.Expect(page.Locator("[data-status-interval='5']")).ToHaveCountAsync(1);
    }
}
