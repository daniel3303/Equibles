using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Repositories;
using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// `ShowHolder` (route `~/Stocks/{ticker}/Holders/{cik}`) has zero coverage in
/// ControllersTests. This pins its second guard: when the ticker resolves but
/// the CIK matches no institutional holder, the action must 404 BEFORE calling
/// `_stockTabService.LoadHolderDetail(stock, holder)` — which would NRE on a
/// null holder. The CIK comes straight from the URL, so any shared/guessed link
/// with a bad CIK hits this path; dropping the `holder == null` check turns a
/// clean 404 into a 500 on a user-facing route.
/// </summary>
public class StocksControllerShowHolderNotFoundTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;

    public StocksControllerShowHolderNotFoundTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task ShowHolder_TickerResolvesButCikUnknown_ReturnsNotFound()
    {
        _dbContext.Set<CommonStock>().Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await _dbContext.SaveChangesAsync();

        var controller = new StocksController(
            new CommonStockRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            institutionalHoldingRepository: null!,
            new DocumentRepository(_dbContext),
            // LoadHolderDetail must never be reached on this path, so the
            // service is intentionally null — a regression that dropped the
            // holder guard would NRE here instead of returning NotFound.
            stockTabService: null!,
            Substitute.For<ILogger<StocksController>>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.ShowHolder("aapl", "0000000000");

        result.Should().BeOfType<NotFoundResult>();
    }
}
