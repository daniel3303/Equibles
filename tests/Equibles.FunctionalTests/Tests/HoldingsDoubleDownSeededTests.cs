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
public class HoldingsDoubleDownSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HoldingsDoubleDownSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task DoubleDown_WithMixedIncreasesAndThreshold_FiltersAndRanksByConviction()
    {
        // Contract: /holdings/double-down shows positions where share-count increase ≥
        // threshold% (default 50%) QoQ, ranked by % increase descending. Positions
        // with no prior shares or below-threshold increase are excluded.
        //
        // Adversarial inputs: absolute deltas are ordered differently from % increases.
        // Filer B has a larger absolute delta (600) than Filer A (200) but a lower %
        // increase (60% vs 200%). A naive sort by absolute delta would fail.
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var nvdaId = Guid.NewGuid();
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
                },
                new CommonStock
                {
                    Id = nvdaId,
                    Ticker = "NVDA",
                    Name = "NVIDIA Corp.",
                    Cik = "0001045810",
                }
            );

            var filerA = new InstitutionalHolder { Cik = "F0000001", Name = "Alpha Capital" };
            var filerB = new InstitutionalHolder { Cik = "F0000002", Name = "Beta Partners" };
            var filerC = new InstitutionalHolder { Cik = "F0000003", Name = "Gamma Holdings" };
            var filerD = new InstitutionalHolder { Cik = "F0000004", Name = "Delta Fund" };
            var filerE = new InstitutionalHolder { Cik = "F0000005", Name = "Epsilon Ventures" };
            db.AddRange(filerA, filerB, filerC, filerD, filerE);

            // Q1 (prior quarter)
            db.Add(MakeHolding(aaplId, filerA.Id, q1, 100, 10_000));
            db.Add(MakeHolding(aaplId, filerB.Id, q1, 1_000, 100_000));
            db.Add(MakeHolding(msftId, filerC.Id, q1, 200, 20_000));
            db.Add(MakeHolding(msftId, filerD.Id, q1, 500, 50_000));

            // Q2 (current quarter)
            // A: 100→300 = +200% (highest conviction, smallest absolute delta)
            db.Add(MakeHolding(aaplId, filerA.Id, q2, 300, 30_000));
            // C: 200→500 = +150%
            db.Add(MakeHolding(msftId, filerC.Id, q2, 500, 50_000));
            // B: 1000→1600 = +60% (meets 50% threshold, largest absolute delta)
            db.Add(MakeHolding(aaplId, filerB.Id, q2, 1_600, 160_000));
            // D: 500→600 = +20% (below 50% threshold — excluded)
            db.Add(MakeHolding(msftId, filerD.Id, q2, 600, 60_000));
            // E: new position in Q2 only — no prior shares, excluded
            db.Add(MakeHolding(nvdaId, filerE.Id, q2, 400, 40_000));

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/holdings/double-down");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Empty state ("Not enough data") must NOT be shown
        await Assertions
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "Not enough data" }))
            .ToHaveCountAsync(0);

        // Position count: 3 (Filer D excluded at 20%, Filer E excluded as new position)
        await Assertions.Expect(page.Locator("text=3 positions")).ToHaveCountAsync(1);

        // Table renders with 3 rows
        var table = page.Locator("[data-testid='double-down-table']");
        await Assertions.Expect(table).ToHaveCountAsync(1);

        var rows = table.Locator("tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(3);

        // Row 0: Alpha Capital / AAPL — highest conviction (200%)
        await Assertions.Expect(rows.Nth(0)).ToContainTextAsync("Alpha Capital");
        await Assertions.Expect(rows.Nth(0)).ToContainTextAsync("AAPL");
        await Assertions.Expect(rows.Nth(0)).ToContainTextAsync(new Regex(@"\+200[.,]0%"));

        // Row 1: Gamma Holdings / MSFT — 150%
        await Assertions.Expect(rows.Nth(1)).ToContainTextAsync("Gamma Holdings");
        await Assertions.Expect(rows.Nth(1)).ToContainTextAsync("MSFT");
        await Assertions.Expect(rows.Nth(1)).ToContainTextAsync(new Regex(@"\+150[.,]0%"));

        // Row 2: Beta Partners / AAPL — 60% (largest absolute delta, but lowest conviction)
        await Assertions.Expect(rows.Nth(2)).ToContainTextAsync("Beta Partners");
        await Assertions.Expect(rows.Nth(2)).ToContainTextAsync("AAPL");
        await Assertions.Expect(rows.Nth(2)).ToContainTextAsync(new Regex(@"\+60[.,]0%"));

        // No pagination needed (3 rows < PageSize of 100)
        await Assertions
            .Expect(page.Locator("[data-testid='double-down-pager']"))
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
