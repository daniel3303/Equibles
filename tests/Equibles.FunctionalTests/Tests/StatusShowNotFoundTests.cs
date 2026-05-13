using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StatusShowNotFoundTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StatusShowNotFoundTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Show_GetForUnknownErrorId_RedirectsToIndexAndRendersFlashError() {
        // StatusController.Show returns RedirectToAction(Index) + flash error when the id
        // resolves to no Error row. The redirect happens via 302 and the flash message
        // round-trips through Session — dropping the IFlashMessage call or returning a bare
        // 404 instead would silently take users to an empty page without explanation. The
        // browser follows the redirect so we can assert both the landed URL and the
        // rendered .alert-error toast in one shot.
        await _web.ResetAndSeedAsync();
        var missingId = Guid.NewGuid();

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync($"/status/show/{missingId}");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);
        page.Url.Should().EndWith("/status",
            "Show must redirect missing-error lookups to the Index action");
        await Assertions.Expect(page.Locator(".alert-error").Filter(new() {
                HasTextString = "Error not found.",
            }))
            .ToHaveCountAsync(1);
    }
}
