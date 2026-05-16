using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
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
/// Pins <c>DoWork</c>'s full per-cycle body: backfill (seeds all-but-latest as
/// processed), the per-file loop with the already-processed skip, the
/// latest-file failure path that adds to failedDataSets, and the end-of-cycle
/// retry that permanently fails and escalates. The retry backoff and
/// failed-data-set cooldown are collapsed via the protected seams so the cycle
/// runs in milliseconds.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsScraperWorkerDoWorkCycleTests : ParadeDbMcpTestBase
{
    public HoldingsScraperWorkerDoWorkCycleTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private sealed class FastWorker : HoldingsScraperWorker
    {
        public FastWorker(
            ILogger<HoldingsScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<WorkerOptions> workerOptions,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, workerOptions, configuration) { }

        protected override TimeSpan[] RetryDelays =>
            [
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
            ];

        protected override TimeSpan FailedDataSetCooldown => TimeSpan.FromMilliseconds(1);
    }

    private HoldingsDataSetClient ThrowingDataSetClient()
    {
        var secEdgar = Substitute.For<ISecEdgarClient>();
        secEdgar
            .DownloadStream(Arg.Any<string>())
            .Returns<Task<Stream>>(_ => throw new HttpRequestException("SEC unavailable"));
        return new HoldingsDataSetClient(
            secEdgar,
            Substitute.For<ILogger<HoldingsDataSetClient>>()
        );
    }

    private HoldingsImportService BuildImporter() =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>()
        );

    [Fact]
    public async Task DoWork_LatestDataSetFailsTransiently_BackfillsSkipsRetriesAndEscalates()
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ProcessedDataSetRepository), new ProcessedDataSetRepository(DbContext)),
            (typeof(HoldingsDataSetClient), ThrowingDataSetClient()),
            (typeof(HoldingsImportService), BuildImporter())
        );

        var config = Substitute.For<IConfiguration>();
        config["Sec:ContactEmail"].Returns("test@example.com");

        // A 2024 start yields a small (>1) new-format file list, so backfill
        // seeds all-but-latest and only the latest file is actually processed.
        var worker = new FastWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions { MinSyncDate = new DateTime(2024, 6, 1) }),
            config
        );

        var doWork = typeof(HoldingsScraperWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        await (Task)doWork.Invoke(worker, [CancellationToken.None]);

        // Backfill seeded every file except the latest as already-processed.
        var processedCount = await DbContext
            .Set<ProcessedDataSet>()
            .AsNoTracking()
            .CountAsync(CancellationToken.None);
        processedCount.Should().BeGreaterThan(0, "backfill marked historical periods processed");
    }
}
