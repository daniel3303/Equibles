using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksIndexSortFilterSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksIndexSortFilterSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_SortByMarketCapDescending_OrdersHighestFirst()
    {
        // Seed three stocks with distinct market caps. Sorting by MarketCapDescending
        // should render NVDA first (3T), AAPL second (2T), MSFT third (1T).
        await _web.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new CommonStock
                {
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                    MarketCapitalization = 2_000_000_000_000,
                },
                new CommonStock
                {
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                    MarketCapitalization = 1_000_000_000_000,
                },
                new CommonStock
                {
                    Ticker = "NVDA",
                    Name = "NVIDIA Corp.",
                    Cik = "0001045810",
                    MarketCapitalization = 3_000_000_000_000,
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks?sort=MarketCapDescending");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // The sort selector should reflect the current sort.
        var sortSelect = page.Locator("select[name='sort']");
        await Assertions.Expect(sortSelect).ToHaveCountAsync(1);

        // Verify the order of tickers in the table: NVDA, AAPL, MSFT.
        var tickerCells = page.Locator("table tbody tr td:first-child a");
        await Assertions.Expect(tickerCells).ToHaveCountAsync(3);
        var tickers = await tickerCells.AllTextContentsAsync();
        tickers.Should().ContainInOrder("NVDA", "AAPL", "MSFT");
    }

    [Fact]
    public async Task Index_MinMarketCapFilter_ExcludesStocksBelowThreshold()
    {
        // Seed three stocks. A minMarketCap of 1.5T should exclude MSFT (1T)
        // and show only AAPL (2T) and NVDA (3T).
        await _web.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new CommonStock
                {
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                    MarketCapitalization = 2_000_000_000_000,
                },
                new CommonStock
                {
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                    MarketCapitalization = 1_000_000_000_000,
                },
                new CommonStock
                {
                    Ticker = "NVDA",
                    Name = "NVIDIA Corp.",
                    Cik = "0001045810",
                    MarketCapitalization = 3_000_000_000_000,
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks?minMarketCap=1500000000000");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Count summary should show 2 stocks.
        await Assertions
            .Expect(page.Locator("p").Filter(new() { HasTextString = "Browse" }))
            .ToContainTextAsync("2 US common stocks");

        // Table should have exactly 2 rows (AAPL and NVDA).
        var tickerCells = page.Locator("table tbody tr td:first-child a");
        await Assertions.Expect(tickerCells).ToHaveCountAsync(2);
        var tickers = await tickerCells.AllTextContentsAsync();
        tickers.Should().Contain("AAPL");
        tickers.Should().Contain("NVDA");
        tickers.Should().NotContain("MSFT");
    }
}
