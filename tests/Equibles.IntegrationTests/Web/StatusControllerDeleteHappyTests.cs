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
/// Sibling to <see cref="StatusControllerShowNotFoundTests"/>. Pins Delete's
/// happy path: the row must be removed from the DB AND the action must
/// redirect to Index (not back to Show). A regression that redirected to
/// Show after delete would render a not-found page for an operator who just
/// successfully deleted an error — visually wrong even though the DB state
/// is right.
/// </summary>
public class StatusControllerDeleteHappyTests : IDisposable
{
    private readonly Equibles.Data.EquiblesDbContext _dbContext;

    public StatusControllerDeleteHappyTests()
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
    public async Task Delete_ExistingErrorId_RemovesRowAndRedirectsToIndex()
    {
        var error = new Error
        {
            Id = Guid.NewGuid(),
            Source = ErrorSource.Other,
            Context = "TestContext",
            Message = "Test error",
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
            Substitute.For<ILogger<StatusController>>()
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        controller.TempData = Substitute.For<ITempDataDictionary>();

        var result = await controller.Delete(error.Id);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(StatusController.Index));
        flashMessage.Received(1).Success("Error deleted.");
        // The row must be gone — a regression that called Detach instead of
        // Remove would still pass the redirect assertion but leak the row.
        (await _dbContext.Set<Error>().AnyAsync(e => e.Id == error.Id))
            .Should()
            .BeFalse();
    }
}
