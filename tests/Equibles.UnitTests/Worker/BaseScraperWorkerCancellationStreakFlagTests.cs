#nullable enable

using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

// ErrorReporter drops OperationCanceledException by type, so a fault streak led by a
// non-shutdown cancellation (an inner HTTP/command timeout that slips past the dedicated
// shutdown catch) must not consume the once-per-streak report flag: "reporting" it writes
// nothing, and a later genuine fault in the same streak would then never reach the Errors
// page. This test pins the gate the same way the threshold tests do — via the log level
// the loop emits (Critical = reported, Warning = deferred).
public class BaseScraperWorkerCancellationStreakFlagTests
{
    [Fact]
    public async Task CancellationLedStreak_StillReportsTheLaterGenuineFault()
    {
        var logger = Substitute.For<ILogger>();
        var parked = new TaskCompletionSource();
        using var worker = new CancellationStreakTestWorker(
            logger,
            doWork: (cycle, stoppingToken) =>
            {
                // Cycle 1: a cancellation NOT tied to the stopping token — the reporter
                // drops it, so it must not consume the streak flag. Cycle 2: a genuine
                // fault in the same streak that must still be reported.
                if (cycle == 1)
                    throw new OperationCanceledException("inner timeout");
                if (cycle == 2)
                    throw new InvalidOperationException("genuine fault");
                parked.TrySetResult();
                return Task.Delay(Timeout.Infinite, stoppingToken);
            }
        );

        await worker.StartAsync(CancellationToken.None);
        await parked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        // Exactly one Errors-table report — the genuine fault; the lead-off cancellation
        // neither reported nor blocked it.
        logger
            .Received(1)
            .Log(
                LogLevel.Critical,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Critical error in")),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
        // The cancelled cycle itself stays a deferred warning.
        logger
            .Received()
            .Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("faulted")),
                Arg.Is<Exception?>(e => e is OperationCanceledException),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    // Deterministic loop driver, mirroring ThresholdTestWorker: threshold 1 so the very
    // first reportable fault escalates, no real waiting between cycles.
    private sealed class CancellationStreakTestWorker : BaseScraperWorker
    {
        private readonly Func<int, CancellationToken, Task> _doWork;
        private int _cycle;

        public CancellationStreakTestWorker(
            ILogger logger,
            Func<int, CancellationToken, Task> doWork
        )
            : base(
                logger,
                Substitute.For<IServiceScopeFactory>(),
                new ErrorReporter(
                    Substitute.For<IServiceScopeFactory>(),
                    Substitute.For<ILogger<ErrorReporter>>()
                )
            )
        {
            _doWork = doWork;
        }

        protected override string WorkerName => "Cancellation streak test";
        protected override TimeSpan SleepInterval => TimeSpan.FromMilliseconds(1);
        protected override TimeSpan ErrorBackoffInterval => TimeSpan.FromMilliseconds(1);
        protected override int ErrorReportThreshold => 1;
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        protected override Task DoWork(CancellationToken stoppingToken) =>
            _doWork(++_cycle, stoppingToken);

        protected override Task WaitForNextCycle(
            TimeSpan interval,
            CancellationToken stoppingToken
        ) => Task.CompletedTask;
    }
}
