using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class ChangelogIndexTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public ChangelogIndexTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_Get_RendersChangelogPageWithMarkdownContent()
    {
        // Pins the /Changelog route — verifies the CHANGELOG.md bundled with the
        // build is rendered to HTML instead of falling back to the GitHub redirect.
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/Changelog");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1").First).ToContainTextAsync("Changelog");

        var content = page.Locator(".changelog-content");
        await Assertions.Expect(content).ToHaveCountAsync(1);
        await Assertions.Expect(content).ToContainTextAsync("Keep a Changelog");
    }
}
