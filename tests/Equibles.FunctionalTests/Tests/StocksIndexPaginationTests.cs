using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksIndexPaginationTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksIndexPaginationTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_GetWithFiveHundredSeededStocks_PaginatesUniquelyAcrossAllTenPages()
    {
        // Seeds 500 alphabetically-named tickers and walks every paginated page in the browser.
        // The Index action sorts by Ticker, applies Skip/Take with pageSize=50, and renders an
        // anchor per row. At volume this surfaces three failure modes that a single-row smoke
        // test cannot: (a) duplicate rows across page boundaries when OrderBy lacks a stable
        // tiebreaker on tied keys, (b) off-by-one truncation on the last page, (c) the EF
        // Search query returning a different shape under volume than under a single row.
        // AutoDetectChangesEnabled is disabled while inserting to keep change-tracker work
        // off the O(n²) path — at 500 rows the difference is seconds.
        const int totalStocks = 500;
        const int pageSize = 50;
        const int totalPages = totalStocks / pageSize;

        await _web.ResetAndSeedAsync(async db =>
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            for (var i = 0; i < totalStocks; i++)
            {
                db.Add(
                    new CommonStock
                    {
                        Ticker = $"TST{i:D5}",
                        Name = $"Test Company {i:D5}",
                        Cik = $"CIK{i:D7}",
                    }
                );
            }
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var seenTickers = new HashSet<string>();

        for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
        {
            var response = await page.GotoAsync($"/stocks?page={pageNumber}");
            response.Should().NotBeNull();
            response!.Status.Should().Be(200);

            await Assertions
                .Expect(page.Locator("p").Filter(new() { HasTextString = "Browse" }))
                .ToContainTextAsync($"{totalStocks:N0} US common stocks");

            var rowTickers = await page.Locator("tbody tr.stock-row td:first-child a")
                .AllInnerTextsAsync();
            rowTickers
                .Should()
                .HaveCount(
                    pageSize,
                    $"page {pageNumber} of {totalPages} must render exactly {pageSize} rows"
                );

            foreach (var ticker in rowTickers)
            {
                seenTickers
                    .Add(ticker)
                    .Should()
                    .BeTrue(
                        $"ticker {ticker} appeared on more than one page — Index's OrderBy(Ticker) lacks a stable tiebreaker"
                    );
            }
        }

        seenTickers.Should().HaveCount(totalStocks);
    }
}
