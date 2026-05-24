using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class MarketPutCallRatioInvalidTypeTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public MarketPutCallRatioInvalidTypeTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task PutCallRatio_InvalidType_Returns404()
    {
        // Contract: /market/putcallratio/{type} returns 404 when the type is not a valid
        // CboePutCallRatioType enum member. The action guards with Enum.TryParse +
        // Enum.IsDefined — the IsDefined check catches numeric strings (e.g. "999") that
        // TryParse accepts but map to no named member.
        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/market/putcallratio/notarealtype");

        response.Should().NotBeNull();
        response!.Status.Should().Be(404);
    }
}
