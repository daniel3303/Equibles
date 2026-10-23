using Equibles.Media.HostedService;
using Equibles.Media.HostedService.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Media;

public class BlobSweepStateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eq-sweep-state-" + Guid.NewGuid().ToString("N")
    );

    public BlobSweepStateStoreTests()
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

    private BlobSweepStateStore CreateStore() => new(_root, NullLogger.Instance);

    [Fact]
    public void Load_WithoutAStateFile_ReturnsAFreshState()
    {
        var state = CreateStore().Load();

        state.LastCompletedAt.Should().BeNull();
        state.Run.Should().BeNull();
    }

    [Fact]
    public void Save_ThenLoad_KeepsTheRunInFlight()
    {
        var saved = new BlobSweepState
        {
            LastCompletedAt = new DateTime(2026, 10, 21, 3, 0, 0, DateTimeKind.Utc),
            LastSlot = 3,
            Run = new BlobSweepRun
            {
                StartedAt = new DateTime(2026, 10, 22, 23, 10, 0, DateTimeKind.Utc),
                Slot = 4,
                Phase = BlobSweepPhase.Purge,
                Position = "blob/sha256/9a/7e",
            },
        };

        CreateStore().Save(saved);

        CreateStore().Load().Should().BeEquivalentTo(saved);
        Directory.GetFiles(_root).Should().ContainSingle();
    }

    // Every sweep step is idempotent, so an unreadable state only repeats a walk.
    [Fact]
    public void Load_UnreadableStateFile_ReturnsAFreshState()
    {
        System.IO.File.WriteAllText(
            Path.Combine(_root, BlobSweepStateStore.FileName),
            "{\"Run\": {\"Phase\": "
        );

        var state = CreateStore().Load();

        state.LastCompletedAt.Should().BeNull();
        state.Run.Should().BeNull();
    }
}
