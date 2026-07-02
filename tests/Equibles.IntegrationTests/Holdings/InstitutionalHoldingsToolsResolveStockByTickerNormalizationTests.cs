using System.Reflection;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingsToolsResolveStockByTickerNormalizationTests : IDisposable
{
    // The MCP ticker contract is uniform across modules: client-supplied
    // tickers must be normalized (Trim + ToUpperInvariant) before the
    // CommonStocks lookup, so `aapl`, `AAPL `, and `AAPL` all resolve to the
    // same stock. McpToolExecutor.NormalizeTicker is the canonical helper
    // (see ShortDataTools.ResolveStockByTicker). Holdings + InsiderTrading
    // each declare their own ResolveStockByTicker; the helper must call
    // NormalizeTicker before GetByTicker so the cross-module contract holds.
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingsTools _tools;

    public InstitutionalHoldingsToolsResolveStockByTickerNormalizationTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _dbContext.Set<CommonStock>().Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc" });
        _dbContext.SaveChanges();

        _tools = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(_dbContext),
                new StockSplitRepository(_dbContext)
            ),
            errorManager: null,
            NullLogger<InstitutionalHoldingsTools>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task<(CommonStock Stock, string Error)> InvokeResolve(string ticker)
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
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
