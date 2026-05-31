using System.Net;
using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins <c>TryProcessDataSet</c>'s "not published yet" arm. The SEC releases each
/// quarterly data set well after the period closes, so a 404 means the file is not
/// filed yet — not a failure. The worker must return success (so the caller neither
/// records a failure nor raises a permanent-failure alarm) and must NOT burn its
/// retry attempts on a file that cannot appear within the cycle; the unprocessed
/// file is naturally retried on the next cycle.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsScraperWorkerNotPublishedTests : ParadeDbMcpTestBase
{
    private const string FileName = "01dec2025-28feb2026_form13f.zip";

    public HoldingsScraperWorkerNotPublishedTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // Production HoldingsScraperWorker with the retry backoff collapsed to ~0 so a
    // regression that retried the 404 would still finish fast (the assertion, not a
    // timeout, is what catches it).
    private sealed class FastRetryWorker : HoldingsScraperWorker
    {
        public FastRetryWorker(
            ILogger<HoldingsScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<WorkerOptions> workerOptions,
            IConfiguration configuration
        )
            : base(
                logger,
                scopeFactory,
                errorReporter,
                workerOptions,
                configuration,
                new HoldingsRescanSignal()
            ) { }

        protected override TimeSpan[] RetryDelays =>
            [
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
            ];
    }

    private HoldingsImportService BuildImporter() =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>(),
            Substitute.For<MassTransit.IBus>()
        );

    [Fact]
    public async Task TryProcessDataSet_NotFound_ReturnsTrueWithoutRetrying()
    {
        var secEdgar = Substitute.For<ISecEdgarClient>();
        secEdgar
            .DownloadStream(Arg.Any<string>())
            .Returns<Task<Stream>>(_ =>
                throw new HttpRequestException("not found", null, HttpStatusCode.NotFound)
            );
        var dataSetClient = new HoldingsDataSetClient(
            secEdgar,
            Substitute.For<ILogger<HoldingsDataSetClient>>()
        );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ProcessedDataSetRepository), new ProcessedDataSetRepository(DbContext)),
            (typeof(HoldingsDataSetClient), dataSetClient),
            (typeof(HoldingsImportService), BuildImporter())
        );

        var config = Substitute.For<IConfiguration>();
        config["Sec:ContactEmail"].Returns("test@example.com");

        var worker = new FastRetryWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            config
        );

        var method = typeof(HoldingsScraperWorker).GetMethod(
            "TryProcessDataSet",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var result = await (Task<bool>)
            method.Invoke(worker, [FileName, new DateOnly(2024, 1, 1), CancellationToken.None]);

        result.Should().BeTrue("a 404 means the data set is not published yet, not a failure");
        await secEdgar.Received(1).DownloadStream(Arg.Any<string>());
    }
}
