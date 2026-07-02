using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.HostedService;
using Equibles.Media.HostedService.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Media;

public class BlobDeletionSweepWorkerDisabledTests
{
    // The sweep deletes data; it must be strictly opt-in. When either flag is off the
    // worker exits immediately without ever creating a DB scope.
    [Fact]
    public async Task StartAsync_WhenSweepDisabled_DoesNoWork()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var worker = new BlobDeletionSweepWorker(
            scopeFactory,
            Options.Create(new FileStorageOptions { Enabled = true, RootPath = "/tmp/x" }),
            Options.Create(new BlobSweepOptions { Enabled = false }),
            NullLogger<BlobDeletionSweepWorker>.Instance
        );

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        scopeFactory.DidNotReceive().CreateScope();
    }
}
