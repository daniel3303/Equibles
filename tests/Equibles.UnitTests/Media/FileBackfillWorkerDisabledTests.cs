using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.HostedService;
using Equibles.Media.HostedService.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Media;

public class FileBackfillWorkerDisabledTests
{
    // When the store or the backfill is disabled, the worker must exit immediately
    // without ever creating a DB scope — the drain is strictly opt-in. CreateAsyncScope
    // is an extension over CreateScope, so asserting the interface member proves the gate.
    [Fact]
    public async Task StartAsync_WhenBackfillDisabled_DoesNoWork()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var worker = new FileBackfillWorker(
            scopeFactory,
            Options.Create(new FileStorageOptions { Enabled = true, RootPath = "/tmp/x" }),
            Options.Create(new FileBackfillOptions { Enabled = false }),
            NullLogger<FileBackfillWorker>.Instance
        );

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        scopeFactory.DidNotReceive().CreateScope();
    }
}
