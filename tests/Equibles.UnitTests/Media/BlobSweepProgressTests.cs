using Equibles.Media.HostedService;
using Equibles.Media.HostedService.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Media;

public class BlobSweepProgressTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eq-sweep-progress-" + Guid.NewGuid().ToString("N")
    );

    public BlobSweepProgressTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private BlobSweepStateStore Store() => new(_root, NullLogger.Instance);

    private static BlobSweepState Running() =>
        new()
        {
            Run = new BlobSweepRun { Slot = 4, Phase = BlobSweepPhase.Purge },
        };

    [Fact]
    public void Finished_InsideTheSaveInterval_KeepsTheSavedPosition()
    {
        var progress = new BlobSweepProgress(Store(), Running(), TimeSpan.FromHours(1));
        progress.Finished(["blob", "sha256", "9a"]);

        progress.Finished(["blob", "sha256", "9b"]);

        progress.Run.Position.Should().Be("blob/sha256/9b");
        Store().Load().Run.Position.Should().Be("blob/sha256/9a");
    }

    [Fact]
    public void Finished_WalkRoot_IsNeverAPosition()
    {
        var progress = new BlobSweepProgress(Store(), Running(), TimeSpan.Zero);
        progress.Finished(["blob", "sha256", "9a"]);

        progress.Finished([]);

        Store().Load().Run.Position.Should().Be("blob/sha256/9a");
    }

    [Fact]
    public void StartPhase_ClearsThePositionAndSaves()
    {
        var progress = new BlobSweepProgress(Store(), Running(), TimeSpan.FromHours(1));
        progress.Finished(["blob", "sha256", "9a"]);

        progress.StartPhase(BlobSweepPhase.Reconcile);

        var saved = Store().Load().Run;
        saved.Phase.Should().Be(BlobSweepPhase.Reconcile);
        saved.Position.Should().BeNull();
    }

    [Fact]
    public void Complete_RecordsTheSlotAndEndsTheRun()
    {
        var progress = new BlobSweepProgress(Store(), Running(), TimeSpan.FromHours(1));

        progress.Complete();

        var saved = Store().Load();
        saved.Run.Should().BeNull();
        saved.LastSlot.Should().Be(4);
        saved.LastCompletedAt.Should().NotBeNull();
    }
}
