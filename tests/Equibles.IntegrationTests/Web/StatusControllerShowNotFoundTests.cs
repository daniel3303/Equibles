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
/// Existing <c>StatusControllerTests</c> in <c>ControllersTests.cs</c> covers
/// <c>Index</c> only. The Show / MarkAsSeen / Delete actions all share the same
/// not-found-redirect pattern (return RedirectToAction(Index) + flash an error).
/// Pins that pattern via Show: a missing error must NOT throw, must NOT render
/// a 500, and must surface the not-found flash to the operator. A regression
/// that dropped the null-check and let <c>View(null)</c> through would render a
/// blank page with the operator unable to tell what went wrong.
/// </summary>
public class StatusControllerShowNotFoundTests : IDisposable
{
    private readonly Equibles.Data.EquiblesDbContext _dbContext;

    public StatusControllerShowNotFoundTests()
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
            new MessagingModuleConfiguration(),
            new CftcModuleConfiguration(),
            new CboeModuleConfiguration()
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Show_NonExistentErrorId_FlashesErrorAndRedirectsToIndex()
    {
        var flashMessage = Substitute.For<IFlashMessage>();
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
            flashMessage,
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

        var result = await controller.Show(Guid.NewGuid());

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(StatusController.Index));
        flashMessage.Received(1).Error("Error not found.");
    }
}
