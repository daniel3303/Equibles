using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.IntegrationTests.Mcp;

public class Form144ProposedSalesToolTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InsiderTradingTools _tools;

    public Form144ProposedSalesToolTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new InsiderTradingModuleConfiguration()
        );
        _tools = new InsiderTradingTools(
            new InsiderTransactionRepository(_dbContext),
            new InsiderOwnerRepository(_dbContext),
            new Form144FilingRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            errorManager: null,
            NullLogger<InsiderTradingTools>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    private CommonStock SeedStock(string ticker = "AAPL", string cik = "0000320193")
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = "Apple Inc.",
            Cik = cik,
        };
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.SaveChanges();
        return stock;
    }

    [Fact]
    public async Task GetProposedSales_StockNotFound_ReturnsNotFoundMessage()
    {
        var result = await _tools.GetProposedSales("ZZZZ");

        result.Should().Contain("ZZZZ");
    }

    [Fact]
    public async Task GetProposedSales_NoFilings_ReturnsEmptyMessage()
    {
        SeedStock();

        var result = await _tools.GetProposedSales("AAPL");

        result.Should().Contain("No Form 144 proposed sales found for AAPL.");
    }

    [Fact]
    public async Task GetProposedSales_WithFilings_RendersTableNewestFirst()
    {
        var stock = SeedStock();
        _dbContext
            .Set<Form144Filing>()
            .Add(MakeFiling(stock.Id, "older", new DateOnly(2026, 1, 5), "ALICE", 1000));
        _dbContext
            .Set<Form144Filing>()
            .Add(
                MakeFiling(stock.Id, "newer", new DateOnly(2026, 5, 27), "LEVINSON ARTHUR D", 50000)
            );
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetProposedSales("AAPL");

        result.Should().Contain("Apple Inc.");
        result.Should().Contain("LEVINSON ARTHUR D");
        result.Should().Contain("50,000"); // invariant-culture grouping
        result.Should().Contain("Director");
        // Newest filing renders before the older one.
        result
            .IndexOf("LEVINSON ARTHUR D", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("ALICE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetProposedSales_RespectsMaxResults()
    {
        var stock = SeedStock();
        for (var i = 0; i < 5; i++)
        {
            _dbContext
                .Set<Form144Filing>()
                .Add(
                    MakeFiling(
                        stock.Id,
                        $"acc-{i}",
                        new DateOnly(2026, 1, 1).AddDays(i),
                        "SELLER",
                        100
                    )
                );
        }
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetProposedSales("AAPL", maxResults: 2);

        result.Should().Contain("Showing 2 most recent notices");
    }

    private static Form144Filing MakeFiling(
        Guid stockId,
        string accession,
        DateOnly filingDate,
        string seller,
        long shares
    )
    {
        return new Form144Filing
        {
            CommonStockId = stockId,
            AccessionNumber = accession,
            FilingDate = filingDate,
            SellerName = seller,
            RelationshipToIssuer = "Director",
            SecurityClassTitle = "Common",
            BrokerName = "Charles Schwab & Co., Inc.",
            SharesToBeSold = shares,
            AggregateMarketValue = shares * 300m,
            SharesOutstanding = 14687356000,
            ApproxSaleDate = filingDate,
            SecuritiesExchangeName = "NASDAQ",
        };
    }
}
