using Equibles.Cftc.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class CftcIndexTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public CftcIndexTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_GetWithEmptyCftcData_RendersFuturesHeaderAndZeroCategoriesDescription()
    {
        // CftcController.Index queries every CftcContract, groups by Category, and joins each row
        // with the latest CftcPositionReport. The view interpolates Categories.Sum/Count into the
        // description. With no seed rows, the group-by yields zero categories and zero contracts —
        // and the description text must reflect that without nulling out. Catches view regressions
        // where the Sum/Count over an empty IEnumerable wouldn't survive a missing null-guard, or
        // a category enum's NameForHumans is invoked before the empty check.
        await _web.ResetAndSeedAsync(); // guarantee empty DB regardless of test ordering
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/futures");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Futures");
        await Assertions
            .Expect(page.Locator("p").Filter(new() { HasTextString = "contracts from" }))
            .ToContainTextAsync("0 contracts from");
    }

    [Fact]
    public async Task Index_GetWithSeededContractAndReport_RendersContractCountAndMarketCode()
    {
        // CftcController.Index has populated-only paths the empty test cannot exercise:
        // the `latestByContractId.TryGetValue(c.Id, out var latest)` lookup hit, the
        // `latest.CommLong - latest.CommShort` and `latest.NonCommLong - latest.NonCommShort`
        // arithmetic, and the non-empty GroupBy(Category) projection. A regression that drops the
        // latest-report join, or breaks the per-category projection, would not be caught by the
        // empty test alone.
        var contractId = Guid.NewGuid();
        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CftcContract
                {
                    Id = contractId,
                    MarketCode = "TEST_ES_FUT",
                    MarketName = "Test E-Mini S&P 500",
                    Category = CftcContractCategory.EquityIndices,
                }
            );
            db.Add(
                new CftcPositionReport
                {
                    CftcContractId = contractId,
                    ReportDate = new DateOnly(2025, 1, 14),
                    OpenInterest = 2_400_000,
                    NonCommLong = 500_000,
                    NonCommShort = 400_000,
                    NonCommSpreads = 80_000,
                    CommLong = 1_200_000,
                    CommShort = 1_100_000,
                    TotalRptLong = 1_700_000,
                    TotalRptShort = 1_500_000,
                    NonRptLong = 200_000,
                    NonRptShort = 100_000,
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/futures");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Futures");
        // Mirrors the empty-state shape, pinned to the populated counter.
        await Assertions
            .Expect(page.Locator("p").Filter(new() { HasTextString = "contracts from" }))
            .ToContainTextAsync("1 contracts from");
        // Seeded market code appears as a link, proving the per-contract projection ran.
        await Assertions
            .Expect(page.Locator("body"))
            .ToContainTextAsync("TEST_ES_FUT");
    }
}
