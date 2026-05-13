using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class AuthLoginTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public AuthLoginTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Login_GetWhenAuthDisabled_RedirectsToHomeInsteadOfRenderingLoginForm() {
        // The fixture configures no "Auth" section, so AuthSettings.IsEnabled is false and the
        // EnvAuthHandler signs every request in as the anonymous principal. AuthController.Login
        // (GET) must short-circuit to Home in that mode — otherwise instances that intentionally
        // run open would expose a non-functional login form. The browser is followed end-to-end so
        // the 302 chain, lowercase-URL routing, anti-forgery cookie issuance, and Home/Index view
        // rendering are all exercised against the real Kestrel host.
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/auth/login");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);
        page.Url.Should().EndWith("/",
            "Login (GET) must redirect to Home when AuthSettings.IsEnabled is false");
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Equibles");
        await Assertions.Expect(page.Locator("form[action*='/auth/login']")).ToHaveCountAsync(0);
    }
}
