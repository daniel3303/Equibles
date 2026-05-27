using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

/// <summary>
/// Pins the startup-stagger added for GH-2241/SEC throttling: a worker with a
/// non-zero StartupDelay must not run its first DoWork until the delay elapses.
/// Uses a gate (TaskCompletionSource) in place of a real delay so the test is
/// deterministic — DoWork cannot run while the gate is open, regardless of
/// scheduling.
/// </summary>
public class BaseScraperWorkerStartupDelayTests
{
    [Fact]
    public async Task ExecuteAsync_WaitsForStartupDelay_BeforeFirstDoWork()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = new GatedStartupWorker(gate);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);

        // The startup delay is still pending → DoWork must not have run.
        await Task.Delay(50);
        worker.DoWorkCallCount.Should().Be(0);

        // Releasing the gate lets the first cycle proceed.
        gate.SetResult();
        await WaitUntilAsync(() => worker.DoWorkCallCount >= 1, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(10);
        }
    }

    private sealed class GatedStartupWorker : BaseScraperWorker
    {
        private readonly TaskCompletionSource _gate;
        private int _doWorkCallCount;

        public int DoWorkCallCount => _doWorkCallCount;

        public GatedStartupWorker(TaskCompletionSource gate)
            : base(
                NullLogger.Instance,
                Substitute.For<IServiceScopeFactory>(),
                new ErrorReporter(
                    Substitute.For<IServiceScopeFactory>(),
                    NullLogger<ErrorReporter>.Instance
                )
            ) => _gate = gate;

        protected override string WorkerName => "GatedStartup";
        protected override TimeSpan SleepInterval => TimeSpan.FromSeconds(30);
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        // Non-zero so ExecuteAsync takes the stagger path; the real wait is the
        // gate, keeping the test deterministic.
        protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(1);

        protected override Task DelayStartup(CancellationToken stoppingToken) => _gate.Task;

        protected override Task DoWork(CancellationToken stoppingToken)
        {
            Interlocked.Increment(ref _doWorkCallCount);
            return Task.CompletedTask;
        }
    }
}
