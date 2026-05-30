using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
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

        var response = await _fixture.Client.GetAsync($"/stocks/{Ticker}/insider-trading");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Cook Timothy D", "the insider-trading tab lists the insider");
    }

    [Fact]
    public async Task GetStocksProposedSales_WithSeededForm144_RendersProposedSalesTab()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Cik = "0000320193",
                Ticker = Ticker,
                Name = "Apple Inc.",
            };
            db.Add(stock);
            db.Add(
                new Form144Filing
                {
                    CommonStockId = stock.Id,
                    AccessionNumber = "0001921094-26-000555",
                    FilingDate = new DateOnly(2026, 5, 27),
                    SellerName = "Levinson Arthur D",
                    RelationshipToIssuer = "Director",
                    SecurityClassTitle = "Common",
                    BrokerName = "Charles Schwab & Co., Inc.",
                    SharesToBeSold = 50_000,
                    AggregateMarketValue = 15_551_085.00m,
                    SharesOutstanding = 14_687_356_000,
                    ApproxSaleDate = new DateOnly(2026, 5, 27),
                    SecuritiesExchangeName = "NASDAQ",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/stocks/{Ticker}/proposed-sales");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Levinson Arthur D", "the proposed-sales tab lists the seller");
        html.Should().Contain("Proposed Sales", "the section tab is labelled");
    }

    [Fact]
    public async Task GetStocksExemptOfferings_WithSeededFormD_RendersExemptOfferingsTab()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Cik = "0000320193",
                Ticker = Ticker,
                Name = "Apple Inc.",
            };
            db.Add(stock);
            db.Add(
                new FormDFiling
                {
                    CommonStockId = stock.Id,
                    AccessionNumber = "0002058722-25-000001",
                    FilingDate = new DateOnly(2025, 2, 28),
                    IsAmendment = false,
                    EntityName = "AJ Boulder Fund LLC",
                    EntityType = "Limited Liability Company",
                    JurisdictionOfInc = "DELAWARE",
                    IndustryGroup = "Pooled Investment Fund",
                    FederalExemptions = "06b, 3C, 3C.7",
                    TotalOfferingAmount = null,
                    IsOfferingAmountIndefinite = true,
                    TotalAmountSold = 0,
                    TotalRemaining = null,
                    IsRemainingIndefinite = true,
                    MinimumInvestmentAccepted = 0,
                    TotalNumberAlreadyInvested = 0,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/stocks/{Ticker}/exempt-offerings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .Contain("Pooled Investment Fund", "the exempt-offerings tab lists the industry");
        html.Should().Contain("Indefinite", "an indefinite offering amount renders as text");
        html.Should().Contain("Exempt Offerings", "the section tab is labelled");
    }
}
