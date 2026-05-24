using System.Text.RegularExpressions;
using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class InstitutionsOverlapMatrixSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InstitutionsOverlapMatrixSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task OverlapMatrix_TwoFilers_RendersMatrixTableAndFundSummary()
    {
        // Pins the /institutions/overlap route — seeds two funds with partially
        // overlapping holdings, verifies the pairwise matrix table renders with
        // shared ticker counts and the fund summary table shows position counts.
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

            // Fund A: AAPL + MSFT (2 positions)
            db.Add(MakeHolding(aaplId, fundA.Id, reportDate, 100, 50_000));
            db.Add(MakeHolding(msftId, fundA.Id, reportDate, 200, 80_000));

            // Fund B: AAPL + NVDA (2 positions, 1 shared with Fund A)
            db.Add(MakeHolding(aaplId, fundB.Id, reportDate, 150, 60_000));
            db.Add(MakeHolding(nvdaId, fundB.Id, reportDate, 300, 90_000));

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync($"/institutions/overlap?ciks={fundACik},{fundBCik}");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("h1").First)
            .ToContainTextAsync("Institution Overlap Matrix");

        // Matrix table should render with data-testid
        var matrixTable = page.Locator("[data-testid='overlap-matrix-table']");
        await Assertions.Expect(matrixTable).ToHaveCountAsync(1);

        // 2 funds → 2 body rows in the matrix
        var matrixRows = matrixTable.Locator("tbody tr");
        await Assertions.Expect(matrixRows).ToHaveCountAsync(2);

        // Diagonal cells show total positions (2 each), off-diagonal shows shared (1)
        var matrixText = await matrixTable.Locator("tbody").TextContentAsync();
        matrixText.Should().Contain("Fund Alpha");
        matrixText.Should().Contain("Fund Beta");

        // Fund summary table should render
        var summaryTable = page.Locator("table[aria-label='Fund summaries']");
        await Assertions.Expect(summaryTable).ToHaveCountAsync(1);

        var summaryRows = summaryTable.Locator("tbody tr");
        await Assertions.Expect(summaryRows).ToHaveCountAsync(2);

        // Fund names and CIKs should appear in summary
        var summaryText = await summaryTable.Locator("tbody").TextContentAsync();
        summaryText.Should().Contain("Fund Alpha");
        summaryText.Should().Contain("Fund Beta");
        summaryText.Should().Contain(fundACik);
        summaryText.Should().Contain(fundBCik);
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
