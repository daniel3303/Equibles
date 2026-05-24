using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class InstitutionsOverlapMatrixMaxCiksTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InstitutionsOverlapMatrixMaxCiksTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task OverlapMatrix_MoreThanMaxCiks_Returns400()
    {
        // Contract: /institutions/overlap rejects requests with more than MaxCiks (10)
        // distinct CIKs by returning 400 BadRequest. The OverlapMatrix action splits
        // comma-separated CIKs before the check, so a single ciks= param with 11
        // values must trigger the guard.
        var ciks = string.Join(",", Enumerable.Range(1, 11).Select(i => $"000000000{i:D1}"));

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync($"/institutions/overlap?ciks={ciks}");

        response.Should().NotBeNull();
        response!.Status.Should().Be(400);
    }
}
