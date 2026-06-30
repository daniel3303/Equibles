#nullable enable

using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

// A worker whose dependency restarts routinely (the embeddings processor — its vLLM sidecar is
// recycled by autoheal/deploys and reloads its model for a cycle or two) would otherwise log a
// hard Error row every faulted cycle, flooding the Errors page with self-healing blips. The
// ErrorReportThreshold defers the Errors-table report until a fault streak proves sustained, and
// records only one row per streak. These tests pin that gating via the log levels the loop emits
// (Critical = reported to the Errors table, Warning = deferred), since ErrorReporter.Report
// swallows its own failures and can't be observed directly through a bare substitute.
public class BaseScraperWorkerErrorReportThresholdTests
{
    [Fact]
    public async Task FaultsBelowThreshold_LogWarningAndBackOff_WithoutReportingToErrorsTable()
    {
        var logger = Substitute.For<ILogger>();
        var parked = new TaskCompletionSource();
        using var worker = new ThresholdTestWorker(
            logger,
            errorReportThreshold: 3,
            doWork: (cycle, stoppingToken) =>
            {
                // Two faults, both below the threshold of 3, then park.
                if (cycle <= 2)
                    throw new InvalidOperationException($"blip {cycle}");
                parked.TrySetResult();
                return Task.Delay(Timeout.Infinite, stoppingToken);
            }
        );

        await worker.StartAsync(CancellationToken.None);
        await parked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        // Sub-threshold faults are warnings, never escalated to the Errors table.
        logger
            .DidNotReceive()
            .Log(
                LogLevel.Critical,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
        logger
            .Received()
            .Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("faulted")),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public async Task FaultStreakReachesThreshold_ReportsExactlyOncePerStreak()
    {
        var logger = Substitute.For<ILogger>();
        var parked = new TaskCompletionSource();
        using var worker = new ThresholdTestWorker(
            logger,
            errorReportThreshold: 3,
            doWork: (cycle, stoppingToken) =>
            {
                // Five consecutive faults (threshold 3): the streak crosses the threshold once
                // and the two faults after it must NOT add more rows.
                if (cycle <= 5)
                    throw new InvalidOperationException($"down {cycle}");
                parked.TrySetResult();
                return Task.Delay(Timeout.Infinite, stoppingToken);
            }
        );

        await worker.StartAsync(CancellationToken.None);
        await parked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        // One outage episode → one Errors-table report, no matter how long it persists.
        logger
            .Received(1)
            .Log(
                LogLevel.Critical,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Critical error in")),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public async Task CleanCycle_ResetsStreak_SoTheNextOutageReportsAgain()
    {
        var logger = Substitute.For<ILogger>();
        var parked = new TaskCompletionSource();
        using var worker = new ThresholdTestWorker(
            logger,
            errorReportThreshold: 3,
            doWork: (cycle, stoppingToken) =>
            {
                // Streak A (cycles 1-3) reaches the threshold and reports once. Cycle 4 succeeds
                // and resets the streak. Streak B (cycles 5-7) reaches the threshold again.
                if (cycle is >= 1 and <= 3 or >= 5 and <= 7)
                    throw new InvalidOperationException($"down {cycle}");
                if (cycle == 4)
                    return Task.CompletedTask;
                parked.TrySetResult();
                return Task.Delay(Timeout.Infinite, stoppingToken);
            }
        );

        await worker.StartAsync(CancellationToken.None);
        await parked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        // Two distinct outage episodes → two reports; the clean cycle between them resets the streak.
        logger
            .Received(2)
            .Log(
                LogLevel.Critical,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Critical error in")),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    // Drives the loop deterministically: WaitForNextCycle returns immediately (no real waiting),
    // DoWork runs the supplied delegate per cycle, and the final cycle parks on an infinite delay
    // until StopAsync cancels it.
    private sealed class ThresholdTestWorker : BaseScraperWorker
    {
        private readonly int _errorReportThreshold;
        private readonly Func<int, CancellationToken, Task> _doWork;
        private int _cycle;

        public ThresholdTestWorker(
            ILogger logger,
            int errorReportThreshold,
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
            _errorReportThreshold = errorReportThreshold;
            _doWork = doWork;
        }

        protected override string WorkerName => "Threshold test";
        protected override TimeSpan SleepInterval => TimeSpan.FromMilliseconds(1);
        protected override TimeSpan ErrorBackoffInterval => TimeSpan.FromMilliseconds(1);
        protected override int ErrorReportThreshold => _errorReportThreshold;
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        protected override Task DoWork(CancellationToken stoppingToken) =>
            _doWork(++_cycle, stoppingToken);

        protected override Task WaitForNextCycle(
            TimeSpan interval,
            CancellationToken stoppingToken
        ) => Task.CompletedTask;
    }
}
