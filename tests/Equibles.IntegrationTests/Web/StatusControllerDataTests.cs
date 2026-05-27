using Equibles.Cboe.Data;
using Equibles.Cboe.Repositories;
using Equibles.Cftc.Data;
using Equibles.Cftc.Repositories;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data;
using Equibles.Congress.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Finra.Data;
using Equibles.Finra.Repositories;
using Equibles.Fred.Data;
using Equibles.Fred.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Messaging;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Web.Controllers;
using Equibles.Web.FlashMessage.Contracts;
using Equibles.Web.Services;
using Equibles.Web.Services.Activity;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins <see cref="StatusController.Data"/> — the JSON endpoint the operator
/// dashboard polls for live status. Two contracts: (1) the shape returns a
/// <c>DataCounts</c> dictionary keyed by name (UI binds to those keys); (2)
/// the response is JSON, not a View. A regression that returned a ViewResult
/// would break every dashboard auto-refresh in production. Asserting on the
/// dictionary key spelling catches the silent rename failure mode where
/// counts become null on the UI without any server error.
/// </summary>
public class StatusControllerDataTests : IDisposable
{
    private readonly Equibles.Data.EquiblesFinancialDbContext _dbContext;

    public StatusControllerDataTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new FinraModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new FredModuleConfiguration(),
            new YahooModuleConfiguration(),
            new ErrorsModuleConfiguration(),
            new CftcModuleConfiguration(),
            new CboeModuleConfiguration()
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Data_EmptyDatabase_ReturnsJsonShapeWithExpectedDataCountKeys()
    {
        var errorRepository = new ErrorRepository(_dbContext);
        var dataCountService = new DataCountService(
            new CommonStockRepository(_dbContext),
            new DocumentRepository(_dbContext),
            new InsiderTransactionRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new InstitutionalHoldingRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new FredObservationRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new CftcPositionReportRepository(_dbContext),
            new CboePutCallRatioRepository(_dbContext),
            new CboeVixDailyRepository(_dbContext)
        );
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();

        var controller = new StatusController(
            errorRepository,
            new ErrorManager(errorRepository),
            Substitute.For<IFlashMessage>(),
            configuration,
            _dbContext,
            dataCountService,
            new ActivityFeedBroadcaster(),
            Substitute.For<ILogger<StatusController>>()
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        controller.TempData = Substitute.For<ITempDataDictionary>();

        var result = await controller.Data();

        var json = result.Should().BeOfType<JsonResult>().Subject;
        // The Json() body is an anonymous object — read DataCounts as a Dictionary<string,int>.
        // Any rename of these keys silently breaks the operator dashboard.
        var bodyType = json.Value!.GetType();
        var dataCounts =
            (IDictionary<string, int>)bodyType.GetProperty("DataCounts")!.GetValue(json.Value)!;
        dataCounts.Should().ContainKey("StockCount");
        dataCounts.Should().ContainKey("DocumentCount");
        dataCounts.Should().ContainKey("CboeVixDailyCount");
        dataCounts["StockCount"].Should().Be(0);
    }
}
