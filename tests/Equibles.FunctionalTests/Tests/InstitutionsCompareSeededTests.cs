using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class InstitutionsCompareSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InstitutionsCompareSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Compare_TwoFilers_RendersOverlapSummaryAndTable()
    {
        // Fund A holds AAPL + MSFT, Fund B holds AAPL + NVDA.
        // Overlap: AAPL is shared, MSFT + NVDA are unique → Jaccard = 1/3 ≈ 33.3%.
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var nvdaId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        var fundACik = "0001067983";
        var fundBCik = "0001603466";

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

            var fundA = new InstitutionalHolder { Cik = fundACik, Name = "Fund Alpha" };
            var fundB = new InstitutionalHolder { Cik = fundBCik, Name = "Fund Beta" };
            db.AddRange(fundA, fundB);

            // Fund A: AAPL + MSFT
            db.Add(MakeHolding(aaplId, fundA.Id, reportDate, 100, 50_000));
            db.Add(MakeHolding(msftId, fundA.Id, reportDate, 200, 80_000));

            // Fund B: AAPL + NVDA
            db.Add(MakeHolding(aaplId, fundB.Id, reportDate, 150, 60_000));
            db.Add(MakeHolding(nvdaId, fundB.Id, reportDate, 300, 90_000));

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync(
            $"/institutions/compare?ciks={fundACik}&ciks={fundBCik}"
        );

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Overlap summary card should render with metrics.
        var summary = page.Locator("[data-testid='compare-overlap-summary']");
        await Assertions.Expect(summary).ToHaveCountAsync(1);

        // Verify stat labels render (decimal separator varies by culture — comma vs period).
        await Assertions.Expect(summary).ToContainTextAsync("Jaccard similarity");
        await Assertions.Expect(summary).ToContainTextAsync("Union positions");
        await Assertions.Expect(summary).ToContainTextAsync("Shared positions");
        var summaryText = await summary.TextContentAsync();
        summaryText.Should().Contain("3"); // union count
        summaryText.Should().Contain("1"); // shared count

        // The overlap table should render with all 3 stocks.
        var table = page.Locator("[data-testid='compare-overlap-table']");
        await Assertions.Expect(table).ToHaveCountAsync(1);

        var bodyRows = table.Locator("tbody tr");
        await Assertions.Expect(bodyRows).ToHaveCountAsync(3);

        // All three tickers should appear in the table.
        var tableText = await table.Locator("tbody").TextContentAsync();
        tableText.Should().Contain("AAPL");
        tableText.Should().Contain("MSFT");
        tableText.Should().Contain("NVDA");
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
