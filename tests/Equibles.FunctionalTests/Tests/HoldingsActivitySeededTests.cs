using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HoldingsActivitySeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HoldingsActivitySeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Activity_TwoQuarters_SegregatesStocksIntoCorrectBoards()
    {
        // Four stocks, each designed to land in exactly one board:
        //   AAPL — shares increased (200→300) → Top Buys
        //   MSFT — shares decreased (500→100) → Top Sells
        //   NVDA — only in latest quarter → New Positions
        //   TSLA — only in prior quarter → Sold-Out Positions
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var nvdaId = Guid.NewGuid();
        var tslaId = Guid.NewGuid();
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
                },
                new CommonStock
                {
                    Id = nvdaId,
                    Ticker = "NVDA",
                    Name = "NVIDIA Corp.",
                    Cik = "0001045810",
                },
                new CommonStock
                {
                    Id = tslaId,
                    Ticker = "TSLA",
                    Name = "Tesla Inc.",
                    Cik = "0001318605",
                }
            );

            var filer = new InstitutionalHolder { Cik = "F0000001", Name = "Test Fund" };
            db.Add(filer);

            // AAPL: increased shares
            db.Add(MakeHolding(aaplId, filer.Id, prior, 200, 40_000));
            db.Add(MakeHolding(aaplId, filer.Id, latest, 300, 60_000));

            // MSFT: decreased shares
            db.Add(MakeHolding(msftId, filer.Id, prior, 500, 100_000));
            db.Add(MakeHolding(msftId, filer.Id, latest, 100, 20_000));

            // NVDA: new position (latest only)
            db.Add(MakeHolding(nvdaId, filer.Id, latest, 150, 45_000));

            // TSLA: sold out (prior only)
            db.Add(MakeHolding(tslaId, filer.Id, prior, 400, 80_000));

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/holdings/activity");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // All four boards should render (not the empty state).
        await Assertions
            .Expect(page.Locator("[data-testid='activity-top-buys']"))
            .ToHaveCountAsync(1);
        await Assertions
            .Expect(page.Locator("[data-testid='activity-top-sells']"))
            .ToHaveCountAsync(1);
        await Assertions
            .Expect(page.Locator("[data-testid='activity-new-positions']"))
            .ToHaveCountAsync(1);
        await Assertions
            .Expect(page.Locator("[data-testid='activity-sold-out-positions']"))
            .ToHaveCountAsync(1);

        // Top Buys: AAPL (increased) + NVDA (new position = 0→150 shares, also a buy).
        // Boards are not mutually exclusive — new/sold-out positions also appear in buys/sells.
        var topBuysRows = page.Locator("[data-testid='activity-top-buys'] tbody tr");
        await Assertions.Expect(topBuysRows).ToHaveCountAsync(2);
        var topBuysText = await page.Locator("[data-testid='activity-top-buys'] tbody")
            .TextContentAsync();
        topBuysText.Should().Contain("AAPL");
        topBuysText.Should().Contain("NVDA");

        // Top Sells: MSFT (decreased) + TSLA (sold out = 400→0 shares, also a sell).
        var topSellsRows = page.Locator("[data-testid='activity-top-sells'] tbody tr");
        await Assertions.Expect(topSellsRows).ToHaveCountAsync(2);
        var topSellsText = await page.Locator("[data-testid='activity-top-sells'] tbody")
            .TextContentAsync();
        topSellsText.Should().Contain("MSFT");
        topSellsText.Should().Contain("TSLA");

        // New Positions: only NVDA (filer didn't hold it in prior quarter).
        var newPositionsRows = page.Locator("[data-testid='activity-new-positions'] tbody tr");
        await Assertions.Expect(newPositionsRows).ToHaveCountAsync(1);
        await Assertions.Expect(newPositionsRows.First).ToContainTextAsync("NVDA");

        // Sold-Out Positions: only TSLA (filer held it in prior quarter, not in latest).
        var soldOutRows = page.Locator("[data-testid='activity-sold-out-positions'] tbody tr");
        await Assertions.Expect(soldOutRows).ToHaveCountAsync(1);
        await Assertions.Expect(soldOutRows.First).ToContainTextAsync("TSLA");

        // Date selector should be present.
        await Assertions.Expect(page.Locator("select#activity-date")).ToHaveCountAsync(1);

        // CSV export link should be visible.
        await Assertions
            .Expect(page.Locator("[data-testid='activity-export-csv']"))
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
