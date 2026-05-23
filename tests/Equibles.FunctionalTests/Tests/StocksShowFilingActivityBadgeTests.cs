using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksShowFilingActivityBadgeTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksShowFilingActivityBadgeTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Show_WithRecentFilings_RendersFilingActivityBadgesWithDistinctCounts()
    {
        // Contract: the stock detail page shows a "13F Activity" section with badges
        // when GetFilingActivitySummary returns non-zero counts. The badges display
        // distinct filing and filer counts from the last 30 days.
        //
        // Adversarial inputs: Filer A files for both AAPL and MSFT under the same
        // accession number (one 13F filing covers multiple stocks). Filer B files
        // separately for AAPL. GetFilingActivitySummary filters by CommonStockId=AAPL,
        // so the AAPL page should see: 2 distinct filings (ACC-001, ACC-002) and
        // 2 distinct filers (A, B). A naive COUNT(*) on AAPL rows would give 2 filings
        // and 2 filers — but the DISTINCT is still critical because in production a
        // single filing can span amendment rows for the same stock.
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            db.Add(
                new CommonStock
                {
                    Id = msftId,
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                }
            );

            var filerA = new InstitutionalHolder { Cik = "F0000001", Name = "Filer A" };
            var filerB = new InstitutionalHolder { Cik = "F0000002", Name = "Filer B" };
            db.AddRange(filerA, filerB);

            // Filer A holds AAPL (filing ACC-001)
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = aaplId,
                    InstitutionalHolderId = filerA.Id,
                    ReportDate = today.AddDays(-15),
                    FilingDate = today.AddDays(-5),
                    Value = 1_000_000,
                    Shares = 100,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "ACC-001",
                }
            );

            // Filer A also holds MSFT under the same filing (proves query filters by stock)
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = msftId,
                    InstitutionalHolderId = filerA.Id,
                    ReportDate = today.AddDays(-15),
                    FilingDate = today.AddDays(-5),
                    Value = 500_000,
                    Shares = 50,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "ACC-001",
                }
            );

            // Filer B holds AAPL under a different filing (ACC-002)
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = aaplId,
                    InstitutionalHolderId = filerB.Id,
                    ReportDate = today.AddDays(-15),
                    FilingDate = today.AddDays(-3),
                    Value = 2_000_000,
                    Shares = 200,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "ACC-002",
                }
            );

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/price");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // The "13F Activity" label must be visible
        await Assertions.Expect(page.Locator("text=13F Activity")).ToHaveCountAsync(1);

        // 2 distinct filings (ACC-001, ACC-002) — not 3
        await Assertions
            .Expect(page.Locator(".badge").Filter(new() { HasTextString = "2 filings" }))
            .ToHaveCountAsync(1);

        // 2 distinct filers (A, B) — not 3
        await Assertions
            .Expect(page.Locator(".badge").Filter(new() { HasTextString = "2 filers" }))
            .ToHaveCountAsync(1);
    }
}
