using System.Reflection;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Finra.HostedService;
using Equibles.Finra.HostedService.Configuration;
using Equibles.Finra.HostedService.Services;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// <see cref="FinraScraperWorker.DoWork"/> was entirely uncovered. Against the
/// real scope/DB harness with an empty database and a far-future MinSyncDate,
/// the short-volume import early-returns (start date past today) and the
/// short-interest import early-returns (no tracked tickers), so the worker's
/// two-phase orchestration runs end-to-end through real services without
/// touching the FINRA API — exercising both phase logs and both scoped
/// resolutions.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinraScraperWorkerDoWorkTests : ParadeDbMcpTestBase
{
    public FinraScraperWorkerDoWorkTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task DoWork_EmptyDbAndFutureMinSyncDate_RunsBothImportsViaEarlyReturn()
    {
        var workerOptions = Options.Create(
            new WorkerOptions { MinSyncDate = new DateTime(2099, 1, 1), TickersToSync = [] }
        );
        var finraClient = Substitute.For<IFinraClient>();
        var errorReporter = new ErrorReporter(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        // One substitute scope provides every type resolved anywhere in the
        // call graph: the worker's two services, plus the repositories the
        // services and TickerMapService resolve from their own nested scopes.
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(DbContext)),
            (typeof(DailyShortVolumeRepository), new DailyShortVolumeRepository(DbContext))
        );
        var tickerMapService = new TickerMapService(scopeFactory);

        var shortVolume = new ShortVolumeImportService(
            scopeFactory,
            Substitute.For<ILogger<ShortVolumeImportService>>(),
            finraClient,
            tickerMapService,
            errorReporter,
            workerOptions
        );
        var shortInterest = new ShortInterestImportService(
            scopeFactory,
            Substitute.For<ILogger<ShortInterestImportService>>(),
            finraClient,
            tickerMapService,
            errorReporter,
            workerOptions
        );

        var workerScopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ShortVolumeImportService), shortVolume),
            (typeof(ShortInterestImportService), shortInterest)
        );

        var worker = new FinraScraperWorker(
            Substitute.For<ILogger<FinraScraperWorker>>(),
            workerScopeFactory,
            errorReporter,
            Options.Create(new FinraScraperOptions())
        );

        var doWork = typeof(FinraScraperWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // Both imports early-return; DoWork must complete without throwing.
        await (Task)doWork.Invoke(worker, [CancellationToken.None]);
    }
}
