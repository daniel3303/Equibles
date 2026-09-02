using System.IO.Compression;
using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Integrations.Sec.Contracts;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBulkBatchPacingTests
{
    [Fact]
    public async Task CompatibilityOverload_ForwardsZeroPause()
    {
        var importer = new RecordingHoldingsImportService();
        using var archive = EmptyArchive();

        await importer.ImportDataSet(archive, new DateOnly(2026, 1, 1), CancellationToken.None);

        importer.ReceivedPause.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task QuarterlyWorker_ForwardsConfiguredPause()
    {
        var importer = new RecordingHoldingsImportService();
        var secClient = Substitute.For<ISecEdgarClient>();
        secClient.DownloadStream(Arg.Any<string>()).Returns(new MemoryStream(EmptyArchiveBytes()));
        var dataSetClient = new HoldingsDataSetClient(
            secClient,
            Substitute.For<ILogger<HoldingsDataSetClient>>()
        );
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(HoldingsDataSetClient)).Returns(dataSetClient);
        provider.GetService(typeof(HoldingsImportService)).Returns(importer);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var worker = new HoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions { HoldingsBulkBatchPauseMilliseconds = 250 }),
            configuration,
            new HoldingsRescanSignal()
        );
        var tryProcess = typeof(HoldingsScraperWorker).GetMethod(
            "TryProcessDataSet",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var result = await (Task<bool>)
            tryProcess!.Invoke(
                worker,
                ["2026q1_form13f.zip", new DateOnly(2026, 1, 1), CancellationToken.None]
            )!;

        result.Should().BeTrue();
        importer.ReceivedPause.Should().Be(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task Complete_WaitsAfterFlushDisposesAndPropagatesCancellation()
    {
        var flushStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseFlush = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        async Task<int> Flush()
        {
            using var scope = new DisposeAction(() => disposed.SetResult());
            flushStarted.SetResult();
            await releaseFlush.Task;
            return 1;
        }

        var completion = HoldingsBatchPacer.Complete(
            Flush(),
            static inserted => inserted > 0,
            TimeSpan.FromHours(1),
            cancellation.Token
        );
        await flushStarted.Task;
        completion.IsCompleted.Should().BeFalse();

        releaseFlush.SetResult();
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        completion.IsCompleted.Should().BeFalse("the cancellable pause follows scope disposal");

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
    }

    private static ZipArchive EmptyArchive() =>
        new(new MemoryStream(EmptyArchiveBytes()), ZipArchiveMode.Read);

    private static byte[] EmptyArchiveBytes()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("EMPTY.tsv");
        }

        return buffer.ToArray();
    }

    private sealed class RecordingHoldingsImportService : HoldingsImportService
    {
        public RecordingHoldingsImportService()
            : base(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<HoldingsImportService>>(),
                Options.Create(new WorkerOptions()),
                Substitute.For<IStockPriceProvider>(),
                Substitute.For<IBus>()
            ) { }

        public TimeSpan? ReceivedPause { get; private set; }

        public override Task<ImportResult> ImportDataSet(
            ZipArchive archive,
            DateOnly minReportDate,
            TimeSpan batchPause,
            CancellationToken cancellationToken
        )
        {
            ReceivedPause = batchPause;
            return Task.FromResult(new ImportResult(0, IsComplete: false));
        }
    }

    private sealed class DisposeAction(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
