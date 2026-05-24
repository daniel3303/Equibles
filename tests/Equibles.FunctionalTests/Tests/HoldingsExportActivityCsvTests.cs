using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HoldingsExportActivityCsvTests
{
    private readonly WebAppFixture _web;

    public HoldingsExportActivityCsvTests(WebAppFixture web)
    {
        _web = web;
    }

    [Fact]
    public async Task Activity_WithTwoQuarters_ReturnsCsvWithBuyAndSellBoards()
    {
        // Pin: the market-wide activity CSV endpoint returns text/csv with Board,
        // Ticker, DeltaShares columns. Seeds two quarters so the endpoint can
        // compute movers: holder increases AAPL (TopBuys) and reduces MSFT (TopSells).
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var q3 = new DateOnly(2024, 9, 30);
        var q4 = new DateOnly(2024, 12, 31);

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

            var holder = new InstitutionalHolder { Cik = "0001067983", Name = "Test Fund" };
            db.Add(holder);

            // Q3: AAPL 500 shares, MSFT 1000 shares
            db.AddRange(
                MakeHolding(aaplId, holder.Id, q3, 500, 100_000),
                MakeHolding(msftId, holder.Id, q3, 1_000, 400_000)
            );

            // Q4: AAPL 800 shares (buy +300), MSFT 600 shares (sell -400)
            db.AddRange(
                MakeHolding(aaplId, holder.Id, q4, 800, 160_000),
                MakeHolding(msftId, holder.Id, q4, 600, 240_000)
            );

            await Task.CompletedTask;
        });

        using var client = new HttpClient { BaseAddress = new Uri(_web.BaseUrl) };
        var response = await client.GetAsync("/holdings/export/activity?date=2024-12-31");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.Should().BeGreaterThanOrEqualTo(2);
        lines[0].Should().Contain("Board");
        lines[0].Should().Contain("Ticker");
        lines[0].Should().Contain("DeltaShares");
        lines[0].Should().Contain("DeltaValue");

        var body = string.Join('\n', lines.Skip(1));
        body.Should().Contain("TopBuys");
        body.Should().Contain("TopSells");
        body.Should().Contain("AAPL");
        body.Should().Contain("MSFT");
    }

    [Fact]
    public async Task Activity_SingleQuarter_Returns404()
    {
        // The endpoint requires at least 2 report dates to compute deltas.
        var stockId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

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

            var holder = new InstitutionalHolder { Cik = "0001067983", Name = "Test Fund" };
            db.Add(holder);

            db.Add(MakeHolding(stockId, holder.Id, reportDate, 500, 100_000));

            await Task.CompletedTask;
        });

        using var client = new HttpClient { BaseAddress = new Uri(_web.BaseUrl) };
        var response = await client.GetAsync("/holdings/export/activity?date=2024-12-31");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
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
