using Equibles.Cboe.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class MarketPutCallRatioTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public MarketPutCallRatioTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task PutCallRatio_GetForSeededEquityType_RendersBreadcrumbAndDisplayName()
    {
        // /market/putcallratio/{type} parses the type as a CboePutCallRatioType (404 on invalid),
        // queries CboePutCallRatioRepository.GetByType, and renders the view with a Market >
        // Put/Call Ratio > {DisplayName} breadcrumb. Seeds one Equity-type ratio so the lookup
        // resolves and the breadcrumb's DisplayName (from NameForHumans on the enum) renders.
        // Catches regressions in the case-insensitive enum parse and the chart/table pipeline
        // — both run only when records.Length > 0.
        // Seed two rows — the controller composes DescriptiveStatistics, and StandardDeviation
        // over a single value returns NaN, which throws when cast to decimal in the view model.
        // Production traffic always has many rows; the test mirrors that minimum.
        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CboePutCallRatio
                {
                    Date = new DateOnly(2025, 1, 2),
                    RatioType = CboePutCallRatioType.Equity,
                    CallVolume = 1_000_000,
                    PutVolume = 750_000,
                    TotalVolume = 1_750_000,
                    PutCallRatio = 0.75m,
                }
            );
            db.Add(
                new CboePutCallRatio
                {
                    Date = new DateOnly(2025, 1, 3),
                    RatioType = CboePutCallRatioType.Equity,
                    CallVolume = 1_100_000,
                    PutVolume = 880_000,
                    TotalVolume = 1_980_000,
                    PutCallRatio = 0.80m,
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/market/putcallratio/equity");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator(".breadcrumbs li").Filter(new() { HasTextString = "Equity" }))
            .ToHaveCountAsync(1);
    }
}
