using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HoldingsExportHoldersCsvTests
{
    private readonly WebAppFixture _web;

    public HoldingsExportHoldersCsvTests(WebAppFixture web)
    {
        _web = web;
    }

    [Fact]
    public async Task Holders_WithSeededHoldings_ReturnsCsvWithCorrectHeadersAndData()
    {
        // The endpoint returns a CSV with one row per institutional holder for a given
        // stock and report date. Pin: correct Content-Type, CSV headers, and seeded data
        // round-trips into the response body.
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

            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = stockId,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = reportDate,
                    FilingDate = reportDate.AddDays(45),
                    Value = 100_000,
                    Shares = 500,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                }
            );

            await Task.CompletedTask;
        });

        using var client = new HttpClient { BaseAddress = new Uri(_web.BaseUrl) };
        var response = await client.GetAsync(
            "/holdings/export/holders?ticker=AAPL&date=2024-12-31"
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.Should().BeGreaterThanOrEqualTo(2);
        lines[0].Should().Contain("Ticker");
        lines[0].Should().Contain("InstitutionalHolderName");
        lines[0].Should().Contain("Shares");
        lines[0].Should().Contain("Value");

        lines[1].Should().Contain("AAPL");
        lines[1].Should().Contain("Test Fund");
        lines[1].Should().Contain("500");
        lines[1].Should().Contain("100000");
    }

    [Fact]
    public async Task Holders_UnknownTicker_Returns404()
    {
        await _web.ResetAndSeedAsync();

        using var client = new HttpClient { BaseAddress = new Uri(_web.BaseUrl) };
        var response = await client.GetAsync("/holdings/export/holders?ticker=ZZZZ");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
}
