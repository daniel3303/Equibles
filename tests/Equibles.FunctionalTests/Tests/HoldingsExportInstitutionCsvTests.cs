using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HoldingsExportInstitutionCsvTests
{
    private readonly WebAppFixture _web;

    public HoldingsExportInstitutionCsvTests(WebAppFixture web)
    {
        _web = web;
    }

    [Fact]
    public async Task Institution_WithSeededHoldings_ReturnsCsvWithPortfolioData()
    {
        // Pin: the institution portfolio CSV endpoint returns text/csv with the correct
        // header row and seeded holding data for one institution across two stocks.
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var holderCik = "0001067983";
        var reportDate = new DateOnly(2024, 12, 31);

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

            var holder = new InstitutionalHolder { Cik = holderCik, Name = "Test Fund" };
            db.Add(holder);

            db.AddRange(
                new InstitutionalHolding
                {
                    CommonStockId = aaplId,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = reportDate,
                    FilingDate = reportDate.AddDays(45),
                    Value = 200_000,
                    Shares = 1_000,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                },
                new InstitutionalHolding
                {
                    CommonStockId = msftId,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = reportDate,
                    FilingDate = reportDate.AddDays(45),
                    Value = 150_000,
                    Shares = 400,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                }
            );

            await Task.CompletedTask;
        });

        using var client = new HttpClient { BaseAddress = new Uri(_web.BaseUrl) };
        var response = await client.GetAsync(
            $"/holdings/export/institution?cik={holderCik}&date=2024-12-31"
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.Should().BeGreaterThanOrEqualTo(3);
        lines[0].Should().Contain("InstitutionalHolderName");
        lines[0].Should().Contain("Ticker");
        lines[0].Should().Contain("Shares");
        lines[0].Should().Contain("Value");

        var body = string.Join('\n', lines.Skip(1));
        body.Should().Contain("AAPL");
        body.Should().Contain("MSFT");
        body.Should().Contain("Test Fund");
    }

    [Fact]
    public async Task Institution_UnknownCik_Returns404()
    {
        await _web.ResetAndSeedAsync();

        using var client = new HttpClient { BaseAddress = new Uri(_web.BaseUrl) };
        var response = await client.GetAsync("/holdings/export/institution?cik=0009999999");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
}
