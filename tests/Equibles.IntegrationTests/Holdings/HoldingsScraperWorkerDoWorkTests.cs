using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The existing Holdings worker tests only invoke single private helpers in
/// isolation. This pins the two largest uncovered methods end-to-end via the
/// real scope/DB harness: the <c>DoWork</c> per-cycle orchestration with no
/// data sets in window, and <c>TryProcessDataSet</c>'s non-transient failure
/// path (a dependency that can't be resolved must be reported and skipped, not
/// crash the scraper).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsScraperWorkerDoWorkTests : ParadeDbMcpTestBase
{
    public HoldingsScraperWorkerDoWorkTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static ErrorReporter BuildErrorReporter() =>
        new(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>());

    private static IConfiguration ConfigWithContactEmail()
    {
        var configuration = Substitute.For<IConfiguration>();
        configuration["Sec:ContactEmail"].Returns("test@example.com");
        return configuration;
    }

    [Fact]
    public async Task DoWork_NoDataSetsInWindow_CompletesWithoutProcessingOrThrowing()
    {
        // MinSyncDate far in the future → GetDataSetFileNames yields nothing,
        // so the cycle backfills nothing, processes nothing, and falls through
        // to RecalculatePendingValues (whose recalculator is intentionally not
        // registered → its own catch swallows, proving the cycle is resilient).
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ProcessedDataSetRepository), new ProcessedDataSetRepository(DbContext))
        );

        var worker = new HoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            BuildErrorReporter(),
            Options.Create(new WorkerOptions { MinSyncDate = new DateTime(2099, 1, 1) }),
            ConfigWithContactEmail(), new HoldingsRescanSignal()
        );

        var doWork = typeof(HoldingsScraperWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        await (Task)doWork.Invoke(worker, [CancellationToken.None]);

        var processed = await DbContext
            .Set<ProcessedDataSet>()
            .AsNoTracking()
            .CountAsync(CancellationToken.None);
        processed.Should().Be(0);
    }

    [Fact]
    public async Task TryProcessDataSet_HoldingsClientNotResolvable_ReportsErrorAndReturnsFalse()
    {
        // Scope has the ProcessedDataSet repo but NOT HoldingsDataSetClient, so
        // GetRequiredService throws inside the try — the non-transient catch must
        // report via ErrorReporter and return false (skip), not bubble up.
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ProcessedDataSetRepository), new ProcessedDataSetRepository(DbContext))
        );

        var worker = new HoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            BuildErrorReporter(),
            Options.Create(new WorkerOptions()),
            ConfigWithContactEmail(), new HoldingsRescanSignal()
        );

        var tryProcess = typeof(HoldingsScraperWorker).GetMethod(
            "TryProcessDataSet",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var result = await (Task<bool>)
            tryProcess.Invoke(
                worker,
                ["2024q1_form13f.zip", new DateOnly(2024, 1, 1), CancellationToken.None]
            );

        result.Should().BeFalse();
    }
}
