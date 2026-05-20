using Equibles.Cboe.Repositories;
using Equibles.Cftc.Repositories;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.Finra.Repositories;
using Equibles.Fred.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Repositories;
using Equibles.Web.Controllers;
using Equibles.Web.FlashMessage.Contracts;
using Equibles.Web.Services;
using Equibles.Web.Services.Activity;
using Equibles.Yahoo.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins <see cref="StatusController.DeleteAll"/> against real Postgres. The
/// underlying ErrorManager calls <c>ExecuteDeleteAsync</c> which EF InMemory
/// doesn't support — the unit-style InMemory siblings cover Show / Delete /
/// MarkAsSeen but cannot reach this action. Two contracts: (1) the entire
/// errors table must be empty after the call regardless of how many rows
/// existed; (2) the action must redirect to Index. A regression that scoped
/// the deletion to a filter would leave stale rows visible after the operator
/// clicked "clear all", a destructive-looking action that silently doesn't
/// destroy.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StatusControllerDeleteAllTests : ParadeDbMcpTestBase
{
    public StatusControllerDeleteAllTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task DeleteAll_ThreeErrorsAcrossSources_WipesEveryRowAndRedirectsToIndex()
    {
        DbContext
            .Set<Error>()
            .Add(
                new Error
                {
                    Source = ErrorSource.Other,
                    Context = "C1",
                    Message = "M1",
                }
            );
        DbContext
            .Set<Error>()
            .Add(
                new Error
                {
                    Source = ErrorSource.DocumentScraper,
                    Context = "C2",
                    Message = "M2",
                }
            );
        DbContext
            .Set<Error>()
            .Add(
                new Error
                {
                    Source = ErrorSource.CongressScraper,
                    Context = "C3",
                    Message = "M3",
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var flashMessage = Substitute.For<IFlashMessage>();
        var errorRepository = new ErrorRepository(DbContext);
        var dataCountService = new DataCountService(
            new CommonStockRepository(DbContext),
            new DocumentRepository(DbContext),
            new InsiderTransactionRepository(DbContext),
            new CongressionalTradeRepository(DbContext),
            new InstitutionalHoldingRepository(DbContext),
            new FailToDeliverRepository(DbContext),
            new FredObservationRepository(DbContext),
            new DailyStockPriceRepository(DbContext),
            new CftcPositionReportRepository(DbContext),
            new CboePutCallRatioRepository(DbContext),
            new CboeVixDailyRepository(DbContext)
        );
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();

        var controller = new StatusController(
            errorRepository,
            new ErrorManager(errorRepository),
            flashMessage,
            configuration,
            DbContext,
            dataCountService,
            new ActivityFeedBroadcaster(),
            Substitute.For<ILogger<StatusController>>()
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        controller.TempData = Substitute.For<ITempDataDictionary>();

        var result = await controller.DeleteAll();

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(StatusController.Index));
        flashMessage.Received(1).Success("All errors deleted.");

        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<Error>().CountAsync()).Should().Be(0);
    }
}
