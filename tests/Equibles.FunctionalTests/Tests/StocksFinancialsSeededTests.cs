using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksFinancialsSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksFinancialsSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Financials_WithSeededFact_RendersLineItemTable()
    {
        // Seed one FinancialFact (Revenues) so the Financials tab has data to display.
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();

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

            db.Add(
                new FinancialConcept
                {
                    Id = conceptId,
                    Taxonomy = FactTaxonomy.UsGaap,
                    Tag = "Revenues",
                    Label = "Revenue",
                }
            );

            db.Add(
                new FinancialFact
                {
                    CommonStockId = stockId,
                    FinancialConceptId = conceptId,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2024, 1, 1),
                    PeriodEnd = new DateOnly(2024, 12, 31),
                    Value = 394_328_000_000m,
                    FiscalYear = 2024,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = new DateOnly(2025, 2, 1),
                    AccessionNumber = "0000320193-25-000001",
                }
            );

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/AAPL/financials");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Statement selector and period selector should render.
        await Assertions.Expect(page.Locator("#financials-statement")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#financials-period")).ToHaveCountAsync(1);

        // The table should have at least one row with "Revenue" label.
        var tableRows = page.Locator("table tbody tr");
        var rowCount = await tableRows.CountAsync();
        rowCount.Should().BeGreaterThanOrEqualTo(1);

        // At least one row should show the seeded "Revenue" concept with its USD value.
        // Multiple rows may match "Revenue" (e.g., "Cost of Revenue"), so assert ≥1.
        var revenueRows = page.Locator("table tbody tr", new() { HasTextString = "Revenue" });
        var revenueCount = await revenueRows.CountAsync();
        revenueCount.Should().BeGreaterThanOrEqualTo(1);
        await Assertions.Expect(revenueRows.First).ToContainTextAsync("USD");
    }
}
