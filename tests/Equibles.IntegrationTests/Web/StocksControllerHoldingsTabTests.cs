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
/// ControllersTests pins Index / Show / Price / ShowDocument-not-found. None of
/// the seven tab actions are exercised. This pins <c>Holdings</c>: an existing
/// ticker must resolve via <c>LoadStock(ticker.ToUpper())</c>, render the shared
/// "Show" view with <c>ActiveTab == "holdings"</c>, and stash a
/// <see cref="HoldingsTabViewModel"/> in <c>ViewData["TabViewModel"]</c>. A
/// regression that pointed the action at the wrong view, mislabelled the active
/// tab, or skipped the case-normalising <c>ToUpper()</c> would 404 valid links
/// or render the wrong tab — invisible to every existing StocksController test.
/// </summary>
public class StocksControllerHoldingsTabTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;

    public StocksControllerHoldingsTabTests()
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
    public async Task Holdings_ExistingTickerLowercase_ReturnsShowViewWithHoldingsTab()
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

        // Lowercase ticker exercises LoadStock's ToUpper() normalisation.
        var result = await controller.Holdings("aapl", date: null);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Show");
        var model = view.Model.Should().BeOfType<StockDetailViewModel>().Subject;
        model.ActiveTab.Should().Be("holdings");
        model.Stock.Ticker.Should().Be("AAPL");
        controller.ViewData["TabViewModel"].Should().BeOfType<HoldingsTabViewModel>();
    }
}
