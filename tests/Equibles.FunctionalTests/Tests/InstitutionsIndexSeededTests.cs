using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class InstitutionsIndexSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InstitutionsIndexSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_SearchByName_FiltersTableToMatchingInstitutions()
    {
        // Seed three institutions with holdings so they appear with non-zero aggregates.
        // Only "Berkshire Hathaway" should match a search for "Berkshire".
        var stockId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );

            var berkshire = new InstitutionalHolder
            {
                Cik = "0001067983",
                Name = "Berkshire Hathaway Inc",
                City = "Omaha",
                StateOrCountry = "NE",
            };
            var vanguard = new InstitutionalHolder
            {
                Cik = "0000102909",
                Name = "Vanguard Group Inc",
                City = "Malvern",
                StateOrCountry = "PA",
            };
            var blackrock = new InstitutionalHolder
            {
                Cik = "0001364742",
                Name = "BlackRock Inc",
                City = "New York",
                StateOrCountry = "NY",
            };

            db.AddRange(berkshire, vanguard, blackrock);

            db.AddRange(
                MakeHolding(stockId, berkshire.Id, reportDate, 200, 5_000_000),
                MakeHolding(stockId, vanguard.Id, reportDate, 300, 8_000_000),
                MakeHolding(stockId, blackrock.Id, reportDate, 250, 7_000_000)
            );

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        // Navigate to the index — all three institutions should appear.
        var response = await page.GotoAsync("/institutions");
        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        var table = page.Locator("[data-testid='institutions-table']");
        await Assertions.Expect(table).ToHaveCountAsync(1);

        var bodyRows = table.Locator("tbody tr");
        await Assertions.Expect(bodyRows).ToHaveCountAsync(3);

        // Type "Berkshire" into the search box and submit the form.
        var searchInput = page.Locator("input[name='search'][placeholder='Institution name...']");
        await searchInput.FillAsync("Berkshire");
        await searchInput
            .Locator("xpath=ancestor::form")
            .Locator("button[type='submit']")
            .ClickAsync();

        // After form submission, only Berkshire Hathaway should appear.
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var filteredRows = page.Locator("[data-testid='institutions-table'] tbody tr");
        await Assertions.Expect(filteredRows).ToHaveCountAsync(1);
        await Assertions.Expect(filteredRows.First).ToContainTextAsync("Berkshire Hathaway");

        // The search value should persist in the input after the round-trip.
        await Assertions.Expect(page.Locator("input[name='search']")).ToHaveValueAsync("Berkshire");

        // A "Clear" link should be visible when filters are active.
        await Assertions.Expect(page.Locator("a:has-text('Clear')")).ToBeVisibleAsync();
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
