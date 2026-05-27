using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data;
using Equibles.Congress.Repositories;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.Controllers;
using Equibles.Web.Services;
using Equibles.Web.ViewModels.Stocks;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Continues the per-tab StocksController pins (Holdings, CongressionalTrades
/// already landed). This pins <c>InsiderTrading</c>: an existing ticker
/// resolves, renders the shared "Show" view with
/// <c>ActiveTab == "insider-trading"</c>, and stashes an
/// <see cref="InsiderTradingTabViewModel"/> in <c>ViewData["TabViewModel"]</c>.
/// The tab actions are copy-paste shaped; a wrong slug or view name pasted here
/// is invisible to every other test.
/// </summary>
public class StocksControllerInsiderTradingTabTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;

    public StocksControllerInsiderTradingTabTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new FinancialFactsModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new FinraModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new YahooModuleConfiguration()
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task InsiderTrading_ExistingTicker_ReturnsShowViewWithInsiderTradingTab()
    {
        _dbContext.Set<CommonStock>().Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await _dbContext.SaveChangesAsync();

        var stockTabService = new StockTabService(
            new InstitutionalHoldingRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new DailyShortVolumeRepository(_dbContext),
            new ShortInterestRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new DocumentRepository(_dbContext),
            new InsiderTransactionRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            new FinancialConceptRepository(_dbContext)
        );
        var controller = new StocksController(
            new CommonStockRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new InstitutionalHoldingRepository(_dbContext),
            new DocumentRepository(_dbContext),
            stockTabService,
            Substitute.For<ILogger<StocksController>>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.InsiderTrading("aapl");

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Show");
        var model = view.Model.Should().BeOfType<StockDetailViewModel>().Subject;
        model.ActiveTab.Should().Be("insider-trading");
        controller.ViewData["TabViewModel"].Should().BeOfType<InsiderTradingTabViewModel>();
    }
}
