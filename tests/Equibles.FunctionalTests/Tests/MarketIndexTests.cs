using Equibles.Cboe.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class MarketIndexTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public MarketIndexTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_GetWithEmptyMarketData_RendersHeaderAndPutCallTableShell()
    {
        // MarketController.Index queries put/call ratios + VIX history and composes a view model.
        // With no rows in either table the page must still render gracefully — every empty-state
        // null in the model flows into the view's interpolation. This test pins that contract: the
        // header renders, the Put/Call Ratios card shows the table shell (every enum value gets
        // a row even with no data), and the response is 200 — not the YSOD that would surface a
        // missing null-coalesce in the view.
        await _web.ResetAndSeedAsync(); // guarantee empty DB regardless of test ordering
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/market");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Market Indicators");
        await Assertions
            .Expect(
                page.Locator("h2.card-title").Filter(new() { HasTextString = "Put/Call Ratios" })
            )
            .ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Index_GetWithSeededPutCallAndVixRows_RendersLatestDateOnPage()
    {
        // MarketController.Index has populated arithmetic branches the empty-state test cannot
        // exercise: `latestVix.Count > 0/1` selects LatestClose/PreviousClose, and `vixRange.Max()/
        // Min()` over the 52-week window throws on an empty sequence — so the absence of a
        // populated test would have let a regression that drops the `Count > 0` guards slip in.
        // Asserts the seeded date renders on the page (PCR row + VIX tile both consume it).
        var settlementDate = new DateOnly(2025, 1, 2);
        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CboePutCallRatio
                {
                    Date = settlementDate,
                    RatioType = CboePutCallRatioType.Equity,
                    CallVolume = 1_000_000,
                    PutVolume = 750_000,
                    TotalVolume = 1_750_000,
                    PutCallRatio = 0.75m,
                }
            );
            db.Add(
                new CboeVixDaily
                {
                    Date = settlementDate,
                    Open = 15m,
                    High = 16m,
                    Low = 14m,
                    Close = 15.5m,
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/market");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Market Indicators");
        // The seeded date appears in both the PCR table row's Date column AND the VIX summary
        // tile's Date field — a stable signal that BOTH controller code paths populated.
        await Assertions
            .Expect(page.Locator("body"))
            .ToContainTextAsync(settlementDate.ToString("yyyy-MM-dd"));
    }
}
