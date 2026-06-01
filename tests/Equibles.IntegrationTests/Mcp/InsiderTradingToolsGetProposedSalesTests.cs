using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class InsiderTradingToolsGetProposedSalesTests : ParadeDbMcpTestBase
{
    public InsiderTradingToolsGetProposedSalesTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private InsiderTradingTools Sut() =>
        new(
            new InsiderTransactionRepository(DbContext),
            new InsiderOwnerRepository(DbContext),
            new Form144FilingRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<InsiderTradingTools>()
        );

    // GetProposedSales had no happy-path coverage — only the skipped negative-maxResults
    // repro (GH-2980) referenced it. Pin that a seeded Form 144 renders into the table with
    // the seller, share count, aggregate market value, approximate sale date, and broker so
    // the success path is guarded against regression (e.g. a dropped or reordered column).
    [Fact]
    public async Task GetProposedSales_StockWithFiling_RendersForm144Row()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<Form144Filing>()
            .Add(
                new Form144Filing
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    AccessionNumber = "0000320193-26-000001",
                    FilingDate = new DateOnly(2026, 4, 20),
                    SellerName = "Jane Insider",
                    RelationshipToIssuer = "Director",
                    SecurityClassTitle = "Common Stock",
                    BrokerName = "Goldman Sachs",
                    SharesToBeSold = 500_000,
                    AggregateMarketValue = 87_500_000,
                    SharesOutstanding = 16_000_000_000,
                    ApproxSaleDate = new DateOnly(2026, 5, 1),
                    SecuritiesExchangeName = "NASDAQ",
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetProposedSales("AAPL");

        result.Should().Contain("Jane Insider");
        result.Should().Contain("| Director |");
        result.Should().Contain("| 500,000 |");
        result.Should().Contain("| $87,500,000 |");
        result.Should().Contain("| 2026-05-01 |");
        result.Should().Contain("| Goldman Sachs |");
    }
}
