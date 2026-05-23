using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class InstitutionsCombinedSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InstitutionsCombinedSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Combined_ThreeFilers_RanksStocksByConsensusCount()
    {
        // AAPL held by all 3 funds, MSFT by 2, NVDA by 1.
        // Consensus order: AAPL (3/3) → MSFT (2/3) → NVDA (1/3).
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var nvdaId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        var cikA = "0001067983";
        var cikB = "0001603466";
        var cikC = "0001336528";

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

            var fundA = new InstitutionalHolder { Cik = cikA, Name = "Fund Alpha" };
            var fundB = new InstitutionalHolder { Cik = cikB, Name = "Fund Beta" };
            var fundC = new InstitutionalHolder { Cik = cikC, Name = "Fund Gamma" };
            db.AddRange(fundA, fundB, fundC);

            // AAPL: all 3 funds
            db.Add(MakeHolding(aaplId, fundA.Id, reportDate, 100, 50_000));
            db.Add(MakeHolding(aaplId, fundB.Id, reportDate, 150, 60_000));
            db.Add(MakeHolding(aaplId, fundC.Id, reportDate, 200, 70_000));

            // MSFT: 2 funds (A + B)
            db.Add(MakeHolding(msftId, fundA.Id, reportDate, 80, 40_000));
            db.Add(MakeHolding(msftId, fundB.Id, reportDate, 120, 50_000));

            // NVDA: 1 fund (C)
            db.Add(MakeHolding(nvdaId, fundC.Id, reportDate, 300, 90_000));

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync(
            $"/institutions/combined?ciks={cikA}&ciks={cikB}&ciks={cikC}"
        );

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Summary card should render with fund count and unique stock count.
        var summary = page.Locator("[data-testid='combined-summary']");
        await Assertions.Expect(summary).ToHaveCountAsync(1);
        await Assertions.Expect(summary).ToContainTextAsync("3 funds combined");
        await Assertions.Expect(summary).ToContainTextAsync("3 unique stocks");
        // AAPL is held by all 3 funds.
        await Assertions.Expect(summary).ToContainTextAsync("1 held by all");

        // Portfolio table should have 3 rows in consensus order.
        var table = page.Locator("[data-testid='combined-portfolio-table']");
        await Assertions.Expect(table).ToHaveCountAsync(1);

        var bodyRows = table.Locator("tbody tr");
        await Assertions.Expect(bodyRows).ToHaveCountAsync(3);

        // First row = AAPL (3/3), second = MSFT (2/3), third = NVDA (1/3).
        await Assertions.Expect(bodyRows.Nth(0)).ToContainTextAsync("AAPL");
        await Assertions.Expect(bodyRows.Nth(0)).ToContainTextAsync("3 / 3");
        await Assertions.Expect(bodyRows.Nth(1)).ToContainTextAsync("MSFT");
        await Assertions.Expect(bodyRows.Nth(1)).ToContainTextAsync("2 / 3");
        await Assertions.Expect(bodyRows.Nth(2)).ToContainTextAsync("NVDA");
        await Assertions.Expect(bodyRows.Nth(2)).ToContainTextAsync("1 / 3");
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
