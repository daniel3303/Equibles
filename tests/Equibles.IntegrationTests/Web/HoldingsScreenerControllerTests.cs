using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the /Holdings/Screener route end-to-end: route → controller → repository
/// (Screen) → view. Asserts the page renders, the filter form survives a round-trip,
/// and seeded stocks appear / disappear correctly based on filter values.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsScreenerControllerTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsScreenerControllerTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetScreener_WithLessThanTwoQuarters_RendersNoDataAlert()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Ticker = "ONEQ",
                Name = "Only One Quarter Corp.",
                Cik = "0000000700",
            };
            var holder = new InstitutionalHolder { Cik = "0007000001", Name = "Solo Holder" };
            db.Add(stock);
            db.Add(holder);
            db.Add(MakeHolding(stock.Id, holder.Id, new DateOnly(2024, 12, 31), 100, 100));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Screener");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, html);
        html.Should().Contain("data-testid=\"screener-heading\"");
        html.Should().Contain("data-testid=\"screener-no-data\"");
    }

    [Fact]
    public async Task GetScreener_NoFilters_RendersAllStocksAcrossTwoQuarters()
    {
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);
        var holderId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0007000002",
                    Name = "Two Quarter Holder",
                }
            );
            var aapl = new CommonStock
            {
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "0000320193",
            };
            var msft = new CommonStock
            {
                Ticker = "MSFT",
                Name = "Microsoft Corp.",
                Cik = "0000789019",
            };
            db.AddRange(aapl, msft);
            db.Add(MakeHolding(aapl.Id, holderId, q1, 1_000, 1_000_000));
            db.Add(MakeHolding(aapl.Id, holderId, q2, 1_500, 1_500_000));
            db.Add(MakeHolding(msft.Id, holderId, q1, 500, 500_000));
            db.Add(MakeHolding(msft.Id, holderId, q2, 600, 600_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Screener");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, html);
        html.Should().Contain("data-testid=\"screener-filters\"");
        html.Should().Contain("data-testid=\"screener-results\"");
        html.Should().Contain(">AAPL<");
        html.Should().Contain(">MSFT<");
    }

    [Fact]
    public async Task GetScreener_MinTotalValueFilter_FiltersResultsAndRoundTripsValue()
    {
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);
        var holderId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0007000003",
                    Name = "Filter Holder",
                }
            );
            var big = new CommonStock
            {
                Ticker = "BIG",
                Name = "Big Co.",
                Cik = "0000007777",
            };
            var small = new CommonStock
            {
                Ticker = "SML",
                Name = "Small Co.",
                Cik = "0000007778",
            };
            db.AddRange(big, small);
            db.Add(MakeHolding(big.Id, holderId, q1, 1, 9_000_000));
            db.Add(MakeHolding(big.Id, holderId, q2, 1, 10_000_000));
            db.Add(MakeHolding(small.Id, holderId, q1, 1, 1_000));
            db.Add(MakeHolding(small.Id, holderId, q2, 1, 1_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Screener?MinTotalValue=5000000");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, html);
        html.Should().Contain(">BIG<");
        html.Should().NotContain(">SML<");
        // Filter value round-trips into the form so reload-from-URL works.
        html.Should().Contain("value=\"5000000\"");
    }

    [Fact]
    public async Task GetScreener_IndustryListPopulatesMultiSelectAndFilters()
    {
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);
        var holderId = Guid.NewGuid();
        var techId = Guid.NewGuid();
        var energyId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new Industry { Id = techId, Name = "Software-Test" });
            db.Add(new Industry { Id = energyId, Name = "Energy-Test" });
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0007000004",
                    Name = "Industry Holder",
                }
            );
            var techStock = new CommonStock
            {
                Ticker = "TKR1",
                Name = "Tech Stock Inc.",
                Cik = "0000008881",
                IndustryId = techId,
            };
            var energyStock = new CommonStock
            {
                Ticker = "TKR2",
                Name = "Energy Stock Inc.",
                Cik = "0000008882",
                IndustryId = energyId,
            };
            db.AddRange(techStock, energyStock);
            db.Add(MakeHolding(techStock.Id, holderId, q1, 1, 1));
            db.Add(MakeHolding(techStock.Id, holderId, q2, 1, 1));
            db.Add(MakeHolding(energyStock.Id, holderId, q1, 1, 1));
            db.Add(MakeHolding(energyStock.Id, holderId, q2, 1, 1));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Holdings/Screener?IndustryIds={techId}");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, html);
        html.Should().Contain("data-testid=\"screener-industry-select\"");
        html.Should().Contain("Software-Test");
        html.Should().Contain(">TKR1<");
        html.Should().NotContain(">TKR2<");
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
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{stockId:N}".Substring(0, 12) + $"-{reportDate:yyyyMMdd}",
        };
}
