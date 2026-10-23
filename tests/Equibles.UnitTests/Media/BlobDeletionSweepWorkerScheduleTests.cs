using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.HostedService;
using Equibles.Media.HostedService.Configuration;
using Equibles.Media.HostedService.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Media;

public class BlobDeletionSweepWorkerScheduleTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    private readonly BlobDeletionSweepWorker _worker = new(
        Substitute.For<IServiceScopeFactory>(),
        Options.Create(new FileStorageOptions()),
        Options.Create(new BlobSweepOptions()),
        NullLogger<BlobDeletionSweepWorker>.Instance
    );

    [Fact]
    public void NextRunDelay_NoCompletedRun_StartsNow()
    {
        _worker.NextRunDelay(new BlobSweepState(), Now).Should().Be(TimeSpan.Zero);
    }

    // A deploy restarts the worker mid-run; the saved run continues instead of waiting a day.
    [Fact]
    public void NextRunDelay_RunInFlight_ResumesNow()
    {
        var state = new BlobSweepState
        {
            LastCompletedAt = Now.AddHours(-1),
            Run = new BlobSweepRun { Phase = BlobSweepPhase.Purge },
        };

        _worker.NextRunDelay(state, Now).Should().Be(TimeSpan.Zero);
    }

    // A deploy an hour after a completed run must not start another one.
    [Fact]
    public void NextRunDelay_CompletedAnHourAgo_WaitsTheRestOfTheInterval()
    {
        var state = new BlobSweepState { LastCompletedAt = Now.AddHours(-1) };

        _worker.NextRunDelay(state, Now).Should().Be(TimeSpan.FromHours(23));
    }

    [Fact]
    public void NextRunDelay_CompletedLongerAgoThanTheInterval_StartsNow()
    {
        var state = new BlobSweepState { LastCompletedAt = Now.AddHours(-30) };

        _worker.NextRunDelay(state, Now).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void NextRunDelay_CompletionAfterTheClock_WaitsAtMostOneInterval()
    {
        var state = new BlobSweepState { LastCompletedAt = Now.AddDays(2) };

        _worker.NextRunDelay(state, Now).Should().Be(TimeSpan.FromHours(24));
    }
}
