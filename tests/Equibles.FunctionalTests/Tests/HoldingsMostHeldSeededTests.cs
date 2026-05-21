using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HoldingsMostHeldSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HoldingsMostHeldSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task MostHeld_GetForTwoQuarterUniverse_RendersRankedTableAndSortSelector()
    {
        // Three stocks, five filers in the latest quarter, ranked by # of filers desc:
        // AAPL = 5 filers, MSFT = 3 filers, NVDA = 1 filer.
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var nvdaId = Guid.NewGuid();
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
                }
            );

            var holders = new InstitutionalHolder[5];
            for (var i = 0; i < holders.Length; i++)
            {
                holders[i] = new InstitutionalHolder
                {
                    Cik = $"M{i + 1:D7}",
                    Name = $"Filer {i + 1}",
                };
                db.Add(holders[i]);
            }

            // AAPL — 5 filers in latest, 1 filer in prior (so prior quarter exists).
            for (var i = 0; i < 5; i++)
                db.Add(MakeHolding(aaplId, holders[i].Id, latest, 100, 200_000));
            db.Add(MakeHolding(aaplId, holders[0].Id, prior, 100, 180_000));

            // MSFT — 3 filers in latest only.
            for (var i = 0; i < 3; i++)
                db.Add(MakeHolding(msftId, holders[i].Id, latest, 50, 100_000));

            // NVDA — 1 filer in latest only.
            db.Add(MakeHolding(nvdaId, holders[0].Id, latest, 10, 20_000));

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/Holdings/MostHeld");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Table renders; empty state does not.
        await Assertions
            .Expect(page.Locator("[data-testid='most-held-table']"))
            .ToHaveCountAsync(1);
        await Assertions
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "No 13F data yet" }))
            .ToHaveCountAsync(0);

        // Date selector and sort selector are both on the page.
        await Assertions.Expect(page.Locator("select#most-held-date")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("select#most-held-sort")).ToHaveCountAsync(1);

        // Three rows in the body, in filer-count order: AAPL → MSFT → NVDA.
        var bodyRows = page.Locator("[data-testid='most-held-table'] tbody tr");
        await Assertions.Expect(bodyRows).ToHaveCountAsync(3);
        await Assertions.Expect(bodyRows.Nth(0)).ToContainTextAsync("AAPL");
        await Assertions.Expect(bodyRows.Nth(1)).ToContainTextAsync("MSFT");
        await Assertions.Expect(bodyRows.Nth(2)).ToContainTextAsync("NVDA");

        // Three rows total, so no pager footer is rendered.
        await Assertions
            .Expect(page.Locator("[data-testid='most-held-pager']"))
            .ToHaveCountAsync(0);
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
