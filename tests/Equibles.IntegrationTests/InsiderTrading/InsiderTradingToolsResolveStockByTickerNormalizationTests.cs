using System.Reflection;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.IntegrationTests.InsiderTrading;

public class InsiderTradingToolsResolveStockByTickerNormalizationTests : IDisposable
{
    // Counterpart of the Holdings normalization pin — keeps the MCP ticker
    // contract uniform across modules. See Holdings'
    // InstitutionalHoldingsToolsResolveStockByTickerNormalizationTests for
    // the full reasoning.
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InsiderTradingTools _tools;

    public InsiderTradingToolsResolveStockByTickerNormalizationTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new InsiderTradingModuleConfiguration()
        );
        _dbContext.Set<CommonStock>().Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc" });
        _dbContext.SaveChanges();

        _tools = new InsiderTradingTools(
            new InsiderTransactionRepository(_dbContext),
            new InsiderOwnerRepository(_dbContext),
            new Form144FilingRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            errorManager: null,
            NullLogger<InsiderTradingTools>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task<(CommonStock Stock, string Error)> InvokeResolve(string ticker)
    {
        var method = typeof(InsiderTradingTools).GetMethod(
            "ResolveStockByTicker",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var task = (Task)method!.Invoke(_tools, [ticker])!;
        await task.ConfigureAwait(false);
        var resultProp = task.GetType().GetProperty("Result")!;
        return ((CommonStock Stock, string Error))resultProp.GetValue(task)!;
    }

    [Theory]
    [InlineData("aapl")]
    [InlineData("AAPL ")]
    [InlineData(" aapl ")]
    public async Task ResolveStockByTicker_NonNormalizedInput_ResolvesToStoredUppercaseTicker(
        string input
    )
    {
        var (stock, error) = await InvokeResolve(input);

        error.Should().BeNull();
        stock.Should().NotBeNull();
        stock.Ticker.Should().Be("AAPL");
    }
}
