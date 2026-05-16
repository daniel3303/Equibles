using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Completes the in-process Razor coverage seam for the Stocks area: the
/// holdings tab, insider-trading tab and holder-detail views render only when
/// their tab data is present, so they were 0%. Each test seeds the minimum
/// graph (stock + holder/holding or insider owner/transaction), GETs the route,
/// and asserts on the row HTML the populated path emits.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StocksTabsViewRenderingTests
{
    private const string Ticker = "AAPL";
    private const string HolderCik = "0001067983";

    private readonly WebHostFixture _fixture;

    public StocksTabsViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    private async Task SeedHoldingsGraph()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Cik = "0000320193",
                Ticker = Ticker,
                Name = "Apple Inc.",
            };
            var holder = new InstitutionalHolder { Cik = HolderCik, Name = "Berkshire Hathaway" };
            db.AddRange(stock, holder);
            db.AddRange(
                new InstitutionalHolding
                {
                    CommonStockId = stock.Id,
                    InstitutionalHolderId = holder.Id,
                    FilingDate = new DateOnly(2026, 1, 31),
                    ReportDate = new DateOnly(2025, 12, 31),
                    Shares = 1_000_000,
                    Value = 50_000_000,
                },
                new InstitutionalHolding
                {
                    CommonStockId = stock.Id,
                    InstitutionalHolderId = holder.Id,
                    FilingDate = new DateOnly(2025, 10, 31),
                    ReportDate = new DateOnly(2025, 9, 30),
                    Shares = 900_000,
                    Value = 42_000_000,
                }
            );
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task GetStocksHoldings_WithSeededInstitutionalHolding_RendersHoldingsTab()
    {
        await SeedHoldingsGraph();

        var response = await _fixture.Client.GetAsync($"/Stocks/{Ticker}/Holdings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Berkshire Hathaway", "the holdings tab lists the holder");
    }

    [Fact]
    public async Task GetStocksHolderDetail_WithSeededHolding_RendersHolderView()
    {
        await SeedHoldingsGraph();

        var response = await _fixture.Client.GetAsync($"/Stocks/{Ticker}/Holders/{HolderCik}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Berkshire Hathaway", "the holder-detail view renders the holder");
        html.Should().Contain(Ticker, "the holder-detail view renders the stock ticker");
    }

    [Fact]
    public async Task GetStocksInsiderTrading_WithSeededTransaction_RendersInsiderTab()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Cik = "0000320193",
                Ticker = Ticker,
                Name = "Apple Inc.",
            };
            var owner = new InsiderOwner
            {
                OwnerCik = "0001214156",
                Name = "Cook Timothy D",
                IsOfficer = true,
                OfficerTitle = "CEO",
            };
            db.AddRange(stock, owner);
            db.Add(
                new InsiderTransaction
                {
                    CommonStockId = stock.Id,
                    InsiderOwnerId = owner.Id,
                    FilingDate = new DateOnly(2026, 1, 5),
                    TransactionDate = new DateOnly(2026, 1, 2),
                    Shares = 50_000,
                    PricePerShare = 190.25m,
                    SharesOwnedAfter = 3_000_000,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = "0001214156-26-000001",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Stocks/{Ticker}/InsiderTrading");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Cook Timothy D", "the insider-trading tab lists the insider");
    }
}
