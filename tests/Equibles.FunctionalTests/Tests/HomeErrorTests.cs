using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HomeErrorTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HomeErrorTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Error_GetWith404_ReturnsNotFoundStatusAndPageNotFoundCopy()
    {
        // HomeController.Error sets Response.StatusCode from the route value and switches
        // ViewData["Title"]/["Description"] on the same value. This pins the 404 branch:
        // both the wire status and the rendered copy must match the switch arms. A regression
        // that returns 500 by default or strips the switch would only surface here.
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/home/error/404");

        response.Should().NotBeNull();
        response!.Status.Should().Be(404);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Page Not Found");
    }
}
