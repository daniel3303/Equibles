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
/// Pins <c>TryProcessDataSet</c>'s transient-retry arms. The download throws a
/// transient exception on every attempt, so the per-attempt backoff block, the
/// HttpRequestException / IOException catch arms, and the "failed all attempts"
/// return-false are all exercised across the full MaxRetries loop. The backoff
/// is collapsed via the <c>RetryDelays</c> seam so the loop runs in
/// milliseconds rather than minutes.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsScraperWorkerRetryArmsTests : ParadeDbMcpTestBase
{
    private const string FileName = "2024q3_form13f.zip";

    public HoldingsScraperWorkerRetryArmsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // Production HoldingsScraperWorker with the retry backoff collapsed to ~0.
    private sealed class FastRetryWorker : HoldingsScraperWorker
    {
        public FastRetryWorker(
            ILogger<HoldingsScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<WorkerOptions> workerOptions,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, workerOptions, configuration, new HoldingsRescanSignal()) { }

        protected override TimeSpan[] RetryDelays =>
            [
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
            ];
    }

    private HoldingsDataSetClient ThrowingDataSetClient(Exception toThrow)
    {
        var secEdgar = Substitute.For<ISecEdgarClient>();
        secEdgar.DownloadStream(Arg.Any<string>()).Returns<Task<Stream>>(_ => throw toThrow);
        return new HoldingsDataSetClient(
            secEdgar,
            Substitute.For<ILogger<HoldingsDataSetClient>>()
        );
    }

    // Resolved by TryProcessDataSet before the (failing) download; never invoked.
    private HoldingsImportService BuildImporter() =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>()
        );

    private FastRetryWorker BuildWorker(Exception downloadException)
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ProcessedDataSetRepository), new ProcessedDataSetRepository(DbContext)),
            (typeof(HoldingsDataSetClient), ThrowingDataSetClient(downloadException)),
            (typeof(HoldingsImportService), BuildImporter())
        );

        var config = Substitute.For<IConfiguration>();
        config["Sec:ContactEmail"].Returns("test@example.com");

        return new FastRetryWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            config
        );
    }

    public static IEnumerable<object[]> TransientExceptions =>
        [
            [new HttpRequestException("network down")],
            [new IOException("disk error")],
        ];

    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public async Task TryProcessDataSet_TransientFailureEveryAttempt_RetriesThenReturnsFalse(
        Exception transient
    )
    {
        var worker = BuildWorker(transient);

        var method = typeof(HoldingsScraperWorker).GetMethod(
            "TryProcessDataSet",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var result = await (Task<bool>)
            method.Invoke(worker, [FileName, new DateOnly(2024, 1, 1), CancellationToken.None]);

        result.Should().BeFalse("every attempt failed transiently — the file is deferred");
    }
}
