using Equibles.Cftc.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class CftcShowTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public CftcShowTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Show_GetWithUnknownMarketCode_Returns404()
    {
        // CftcController.Show has two not-found branches: empty/whitespace marketCode, and a
        // valid code that doesn't match any contract. With no seed rows, every code falls into
        // the second branch. This test pins the wire-level NotFound contract — a regression
        // that returns a 200 with an empty view (e.g., a controller refactor that drops the
        // null-check on the contract lookup) would only surface here.
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/futures/NOT_A_REAL_MARKET_CODE");

        response.Should().NotBeNull();
        response!.Status.Should().Be(404);
    }

    [Fact]
    public async Task Show_GetWithKnownMarketCodeAndReports_RendersMarketHeaderAndLatestPositioning()
    {
        // CftcController.Show with a matching contract goes through repository.GetByMarketCode +
        // reports load + viewmodel composition + view render. The 404-only test cannot exercise
        // any of that. Seeds two reports so the controller's `latest.CommLong - latest.CommShort`
        // arithmetic runs and the view's `if (latest != null)` Latest-Positioning block renders.
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
            db.Add(
                new CftcPositionReport
                {
                    CftcContractId = contractId,
                    ReportDate = new DateOnly(2025, 1, 7),
                    OpenInterest = 2_300_000,
                    NonCommLong = 480_000,
                    NonCommShort = 410_000,
                    NonCommSpreads = 70_000,
                    CommLong = 1_180_000,
                    CommShort = 1_120_000,
                    TotalRptLong = 1_660_000,
                    TotalRptShort = 1_530_000,
                    NonRptLong = 190_000,
                    NonRptShort = 90_000,
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/futures/TEST_ES_FUT");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Test E-Mini S&P 500");
        await Assertions
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "Latest Positioning" }))
            .ToHaveCountAsync(1);
    }
}
