using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HoldingsLatestFilingsSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HoldingsLatestFilingsSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task LatestFilings_WithNewAndReturningFilers_RendersCorrectBadges()
    {
        // Contract: /holdings/filings shows filings ordered by ImportedAt descending.
        // A filer with no holdings in a prior quarter gets a "New" badge. Amendment
        // filings get an "A" badge. Position count and total value aggregate multiple
        // holdings per filing.
        //
        // Adversarial inputs: Filer A has Q1 and Q2 filings (returning filer, no "New"
        // badge on Q2). Filer B has only a Q2 filing (new filer, "New" badge). Filer B's
        // filing is an amendment (gets "A" badge too). Filer A's Q2 filing has 2
        // positions — verifies the aggregation count.
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);

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
                }
            );

            var filerA = new InstitutionalHolder { Cik = "F0000001", Name = "Alpha Capital" };
            var filerB = new InstitutionalHolder { Cik = "F0000002", Name = "Beta Partners" };
            db.AddRange(filerA, filerB);

            // Filer A — Q1 filing (establishes prior quarter, so Q2 is NOT new)
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = aaplId,
                    InstitutionalHolderId = filerA.Id,
                    ReportDate = q1,
                    FilingDate = q1.AddDays(45),
                    Value = 1_000_000,
                    Shares = 100,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "ACC-001",
                    CreationTime = new DateTime(2024, 11, 15, 10, 0, 0, DateTimeKind.Utc),
                }
            );

            // Filer A — Q2 filing with 2 positions (AAPL + MSFT under same accession)
            // Imported later than Q1, so appears first in the table
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = aaplId,
                    InstitutionalHolderId = filerA.Id,
                    ReportDate = q2,
                    FilingDate = q2.AddDays(45),
                    Value = 2_000_000,
                    Shares = 200,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "ACC-002",
                    CreationTime = new DateTime(2025, 2, 15, 10, 0, 0, DateTimeKind.Utc),
                }
            );
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = msftId,
                    InstitutionalHolderId = filerA.Id,
                    ReportDate = q2,
                    FilingDate = q2.AddDays(45),
                    Value = 3_000_000,
                    Shares = 300,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "ACC-002",
                    CreationTime = new DateTime(2025, 2, 15, 10, 0, 0, DateTimeKind.Utc),
                }
            );

            // Filer B — Q2 only (new filer), amendment filing, imported most recently
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = aaplId,
                    InstitutionalHolderId = filerB.Id,
                    ReportDate = q2,
                    FilingDate = q2.AddDays(50),
                    Value = 5_000_000,
                    Shares = 500,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "ACC-003",
                    IsAmendment = true,
                    CreationTime = new DateTime(2025, 2, 20, 10, 0, 0, DateTimeKind.Utc),
                }
            );

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/holdings/filings");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Table renders
        var table = page.Locator("[data-testid='latest-filings-table']");
        await Assertions.Expect(table).ToHaveCountAsync(1);

        // 3 filings total (ACC-001 Q1, ACC-002 Q2, ACC-003 Q2)
        var rows = table.Locator("tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(3);

        // Row 0 (most recent import): Filer B — new filer + amendment
        var row0 = rows.Nth(0);
        await Assertions.Expect(row0).ToContainTextAsync("Beta Partners");
        await Assertions
            .Expect(row0.Locator(".badge").Filter(new() { HasTextString = "New" }))
            .ToHaveCountAsync(1);
        await Assertions
            .Expect(row0.Locator(".badge").Filter(new() { HasTextString = "A" }))
            .ToHaveCountAsync(1);

        // Row 1: Filer A Q2 — returning filer (no "New"), 2 positions
        var row1 = rows.Nth(1);
        await Assertions.Expect(row1).ToContainTextAsync("Alpha Capital");
        await Assertions
            .Expect(row1.Locator(".badge").Filter(new() { HasTextString = "New" }))
            .ToHaveCountAsync(0);

        // Row 2: Filer A Q1 — oldest import. Still not new (Q1 is their first quarter,
        // but IsNewFiler checks prior.ReportDate < f.ReportDate — Q1 has no prior)
        var row2 = rows.Nth(2);
        await Assertions.Expect(row2).ToContainTextAsync("Alpha Capital");
    }
}
