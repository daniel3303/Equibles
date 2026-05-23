using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class InstitutionProfileSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InstitutionProfileSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Institution_WithTwoQuarters_RendersSummaryAllocationAndActivityBuckets()
    {
        // Seed two quarters of holdings across two industries. Between Q3 and Q4:
        //   - AAPL: 500 → 800 shares (Increased)
        //   - MSFT: 300 → 300 shares (Unchanged — won't appear in activity)
        //   - NVDA: 0 → 200 shares   (Initiated — new position in Q4)
        //   - GOOG: 400 → 0 shares   (Exited — held in Q3, gone in Q4)
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var nvdaId = Guid.NewGuid();
        var googId = Guid.NewGuid();
        var holderCik = "0001067983";
        var q3 = new DateOnly(2024, 9, 30);
        var q4 = new DateOnly(2024, 12, 31);

        var techIndustry = new Industry { Name = "Technology" };
        var searchIndustry = new Industry { Name = "Internet Services" };

        await _web.ResetAndSeedAsync(async db =>
        {
            db.AddRange(techIndustry, searchIndustry);

            db.AddRange(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                    IndustryId = techIndustry.Id,
                },
                new CommonStock
                {
                    Id = msftId,
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                    IndustryId = techIndustry.Id,
                },
                new CommonStock
                {
                    Id = nvdaId,
                    Ticker = "NVDA",
                    Name = "NVIDIA Corp.",
                    Cik = "0001045810",
                    IndustryId = techIndustry.Id,
                },
                new CommonStock
                {
                    Id = googId,
                    Ticker = "GOOG",
                    Name = "Alphabet Inc.",
                    Cik = "0001652044",
                    IndustryId = searchIndustry.Id,
                }
            );

            var holder = new InstitutionalHolder
            {
                Cik = holderCik,
                Name = "Test Capital Management",
            };
            db.Add(holder);

            // Q3 holdings: AAPL, MSFT, GOOG
            db.AddRange(
                MakeHolding(aaplId, holder.Id, q3, 500, 100_000),
                MakeHolding(msftId, holder.Id, q3, 300, 120_000),
                MakeHolding(googId, holder.Id, q3, 400, 60_000)
            );

            // Q4 holdings: AAPL (increased), MSFT (unchanged), NVDA (initiated), GOOG (exited)
            db.AddRange(
                MakeHolding(aaplId, holder.Id, q4, 800, 160_000),
                MakeHolding(msftId, holder.Id, q4, 300, 125_000),
                MakeHolding(nvdaId, holder.Id, q4, 200, 90_000)
            );

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync($"/institutions/{holderCik}");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Heading renders holder name.
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Test Capital Management");

        // Portfolio summary card renders with stats from Q4.
        var summary = page.Locator("[data-testid='institution-summary']");
        await Assertions.Expect(summary).ToHaveCountAsync(1);
        await Assertions.Expect(summary).ToContainTextAsync("Reported AUM");
        await Assertions.Expect(summary).ToContainTextAsync("# Positions");
        await Assertions.Expect(summary).ToContainTextAsync("Top 10 concentration");
        await Assertions.Expect(summary).ToContainTextAsync("QoQ turnover");
        await Assertions.Expect(summary).ToContainTextAsync("Quarters reported");

        // Industry allocation renders with both industries.
        var allocation = page.Locator("[data-testid='institution-industry-allocation']");
        await Assertions.Expect(allocation).ToHaveCountAsync(1);
        await Assertions.Expect(allocation).ToContainTextAsync("Sector allocation");
        var allocationText = await allocation.TextContentAsync();
        allocationText.Should().Contain("Technology");

        // Quarterly activity renders with correct buckets.
        var activity = page.Locator("[data-testid='institution-quarterly-activity']");
        await Assertions.Expect(activity).ToHaveCountAsync(1);

        // Initiated bucket should show NVDA.
        var initiated = page.Locator("[data-testid='activity-bucket-initiated']");
        await Assertions.Expect(initiated).ToHaveCountAsync(1);
        var initiatedText = await initiated.TextContentAsync();
        initiatedText.Should().Contain("NVDA");

        // Increased bucket should show AAPL.
        var increased = page.Locator("[data-testid='activity-bucket-increased']");
        await Assertions.Expect(increased).ToHaveCountAsync(1);
        var increasedText = await increased.TextContentAsync();
        increasedText.Should().Contain("AAPL");

        // Exited bucket should show GOOG.
        var exited = page.Locator("[data-testid='activity-bucket-exited']");
        await Assertions.Expect(exited).ToHaveCountAsync(1);
        var exitedText = await exited.TextContentAsync();
        exitedText.Should().Contain("GOOG");

        // Backtest and CSV links render when holdings exist.
        await Assertions
            .Expect(page.Locator("[data-testid='institution-backtest-link']"))
            .ToHaveCountAsync(1);
        await Assertions
            .Expect(page.Locator("[data-testid='institution-export-csv']"))
            .ToHaveCountAsync(1);
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
