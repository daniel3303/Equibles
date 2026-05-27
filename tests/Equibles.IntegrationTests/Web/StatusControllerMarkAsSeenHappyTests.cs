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
using Equibles.Errors.Data.Models;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins <see cref="StatusController.MarkAsSeen"/> happy path alongside the
/// sibling Show / Delete pins. Unlike Delete (redirects to Index because the
/// row no longer exists), MarkAsSeen must redirect back to Show so the
/// operator can confirm the row is now marked. A regression that copy-pasted
/// the Delete redirect would either 404 if Show is taught to refuse seen
/// rows, or hide the confirmation. Also pins that the DB Seen column flips
/// true so the visual badge in the index falls off.
/// </summary>
public class StatusControllerMarkAsSeenHappyTests : IDisposable
{
    private readonly Equibles.Data.EquiblesFinancialDbContext _dbContext;

    public StatusControllerMarkAsSeenHappyTests()
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
    public async Task MarkAsSeen_ExistingUnseenError_FlipsSeenAndRedirectsToShow()
    {
        var error = new Error
        {
            Id = Guid.NewGuid(),
            Source = ErrorSource.Other,
            Context = "TestContext",
            Message = "Test error",
            Seen = false,
        };
        _dbContext.Set<Error>().Add(error);
        await _dbContext.SaveChangesAsync();

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

        var result = await controller.MarkAsSeen(error.Id);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(StatusController.Show));
        redirect.RouteValues!["id"].Should().Be(error.Id);
        flashMessage.Received(1).Success("Error marked as seen.");
        var refreshed = await _dbContext
            .Set<Error>()
            .AsNoTracking()
            .SingleAsync(e => e.Id == error.Id);
        refreshed.Seen.Should().BeTrue();
    }
}
