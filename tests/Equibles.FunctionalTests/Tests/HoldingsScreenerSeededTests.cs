using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HoldingsScreenerSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HoldingsScreenerSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Screener_MinFilerCountFilter_ShowsOnlyMatchingStocks()
    {
        // AAPL held by 3 filers, MSFT by 1 filer across two quarters.
        // A MinFilerCount=2 filter should show only AAPL.
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var prior = new DateOnly(2024, 9, 30);
        var latest = new DateOnly(2024, 12, 31);

        await _web.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                },
                new CommonStock
                {
                    Id = msftId,
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                }
            );

            var filers = new InstitutionalHolder[3];
            for (var i = 0; i < filers.Length; i++)
            {
                filers[i] = new InstitutionalHolder
                {
                    Cik = $"F{i + 1:D7}",
                    Name = $"Fund {i + 1}",
                };
                db.Add(filers[i]);
            }

            // AAPL: 3 filers in both quarters
            for (var i = 0; i < 3; i++)
            {
                db.Add(MakeHolding(aaplId, filers[i].Id, prior, 100, 30_000));
                db.Add(MakeHolding(aaplId, filers[i].Id, latest, 120, 36_000));
            }

            // MSFT: 1 filer in both quarters
            db.Add(MakeHolding(msftId, filers[0].Id, prior, 50, 10_000));
            db.Add(MakeHolding(msftId, filers[0].Id, latest, 60, 12_000));

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        // Navigate to the screener — both stocks should appear unfiltered.
        var response = await page.GotoAsync("/holdings/screener");
        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("[data-testid='screener-filters']"))
            .ToHaveCountAsync(1);
        await Assertions
            .Expect(page.Locator("[data-testid='screener-results']"))
            .ToHaveCountAsync(1);

        var resultRows = page.Locator("[data-testid='screener-results'] tbody tr");
        await Assertions.Expect(resultRows).ToHaveCountAsync(2);

        // Apply MinFilerCount=2 filter.
        await page.Locator("input[name='MinFilerCount']").FillAsync("2");
        await page.Locator("button:has-text('Apply filters')").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Only AAPL (3 filers) should survive; MSFT (1 filer) is excluded.
        var filteredRows = page.Locator("[data-testid='screener-results'] tbody tr");
        await Assertions.Expect(filteredRows).ToHaveCountAsync(1);
        await Assertions.Expect(filteredRows.First).ToContainTextAsync("AAPL");

        // The filter value should persist in the input after the round-trip.
        await Assertions.Expect(page.Locator("input[name='MinFilerCount']")).ToHaveValueAsync("2");

        // CSV export link should be visible.
        await Assertions
            .Expect(page.Locator("[data-testid='screener-export-csv']"))
            .ToBeVisibleAsync();
    }

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Value = value,
            Shares = shares,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
        };
}
