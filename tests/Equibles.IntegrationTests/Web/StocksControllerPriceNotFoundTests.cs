using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// `Price` is the canonical landing tab (`~/Stocks/{ticker}` redirects here).
/// Existing tab tests only feed a seeded ticker. The unknown-ticker guard is
/// the highest-traffic 404 path — typos and stale links hit it constantly. It
/// must return NotFound BEFORE BuildStockViewModel / LoadPriceTab, which would
/// NRE on a null stock and turn a clean 404 into a 500 on the main page.
/// </summary>
public class StocksControllerPriceNotFoundTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;

    public StocksControllerPriceNotFoundTests()
    {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Price_UnknownTicker_ReturnsNotFoundBeforeTouchingTabService()
    {
        // A different stock exists so the lookup is a real miss, not an empty DB.
        _dbContext
            .Set<CommonStock>()
            .Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await _dbContext.SaveChangesAsync();

        var controller = new StocksController(
            new CommonStockRepository(_dbContext),
            institutionalHolderRepository: null!,
            institutionalHoldingRepository: null!,
            documentRepository: null!,
            // Must 404 before any tab load — a dropped null-stock guard would
            // NRE here instead of returning NotFound.
            stockTabService: null!,
            Substitute.For<ILogger<StocksController>>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Price("ZZZZ");

        result.Should().BeOfType<NotFoundResult>();
    }
}
