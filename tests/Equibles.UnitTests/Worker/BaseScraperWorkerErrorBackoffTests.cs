using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

// A cycle whose DoWork throws (e.g. a transient DB outage while a deploy bounces the
// database) must back off briefly and retry, NOT sleep the full SleepInterval. Before
// this, a single throw parked the worker for a whole SleepInterval (6h for the IR-flow
// and webcast workers), abandoning any backlog it was draining — the worker stayed "up"
// but did no work for hours. These tests pin the backoff value, its exponential growth,
// the cap, and the reset-on-success, by capturing the interval the loop chose rather than
// racing a real clock.
public class BaseScraperWorkerErrorBackoffTests
{
    [Fact]
    public async Task FaultedCycle_WaitsErrorBackoff_NotFullSleepInterval()
    {
        var backoff = TimeSpan.FromMilliseconds(5);
        var parked = new TaskCompletionSource();
        using var worker = new TestWorker(
            sleepInterval: TimeSpan.FromHours(1),
            errorBackoffInterval: backoff,
            doWork: (cycle, _, stoppingToken) =>
            {
                if (cycle == 1)
                    throw new InvalidOperationException("boom");
                parked.TrySetResult();
                return Task.Delay(Timeout.Infinite, stoppingToken);
            }
        );

        await worker.StartAsync(CancellationToken.None);
        await parked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        worker
            .Waits.Should()
            .ContainSingle("the faulted cycle chooses exactly one wait before retrying")
            .Which.Should()
            .Be(backoff, "a faulted cycle backs off briefly, not for the full SleepInterval");
    }

    [Fact]
    public async Task FaultedCycle_UsesErrorBackoff_EvenWhenContinuationRequested()
    {
        var backoff = TimeSpan.FromMilliseconds(5);
        var parked = new TaskCompletionSource();
        using var worker = new TestWorker(
            sleepInterval: TimeSpan.FromHours(1),
            errorBackoffInterval: backoff,
            continuationInterval: TimeSpan.FromHours(2),
            doWork: (cycle, worker, stoppingToken) =>
            {
                if (cycle == 1)
                {
                    // Request a fast continuation, then fault after it: the fault must win.
                    worker.CallRequestImmediateContinuation();
                    throw new InvalidOperationException("boom after requesting continuation");
                }
                parked.TrySetResult();
                return Task.Delay(Timeout.Infinite, stoppingToken);
            }
        );

        await worker.StartAsync(CancellationToken.None);
        await parked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        worker
            .Waits.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                backoff,
                "a faulted cycle backs off even if it had requested an immediate continuation"
            );
    }

    [Fact]
    public async Task ConsecutiveFaults_BackOffExponentially_CappedAtMax()
    {
        var parked = new TaskCompletionSource();
        using var worker = new TestWorker(
            sleepInterval: TimeSpan.FromHours(6),
            errorBackoffInterval: TimeSpan.FromMinutes(1),
            maxErrorBackoffInterval: TimeSpan.FromMinutes(4),
            doWork: (cycle, _, stoppingToken) =>
            {
                if (cycle <= 5)
                    throw new InvalidOperationException($"boom {cycle}");
                parked.TrySetResult();
                return Task.Delay(Timeout.Infinite, stoppingToken);
            }
        );

        await worker.StartAsync(CancellationToken.None);
        await parked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        // 1 * 2^0, 2^1, 2^2, then capped at 4m (8m, 16m clamped down).
        worker
            .Waits.Should()
            .Equal(
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(2),
                TimeSpan.FromMinutes(4),
                TimeSpan.FromMinutes(4),
                TimeSpan.FromMinutes(4)
            );
    }

    [Fact]
    public async Task SuccessfulCycle_ResetsBackoff_SoNextFaultStartsFromBase()
    {
        var sleep = TimeSpan.FromHours(6);
        var parked = new TaskCompletionSource();
        using var worker = new TestWorker(
            sleepInterval: sleep,
            errorBackoffInterval: TimeSpan.FromMinutes(1),
            maxErrorBackoffInterval: TimeSpan.FromHours(1),
            doWork: (cycle, _, stoppingToken) =>
            {
                // fault, fault, succeed, fault, then park.
                if (cycle is 1 or 2 or 4)
                    throw new InvalidOperationException($"boom {cycle}");
                if (cycle == 3)
                    return Task.CompletedTask;
                parked.TrySetResult();
                return Task.Delay(Timeout.Infinite, stoppingToken);
            }
        );

        await worker.StartAsync(CancellationToken.None);
        await parked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        // Fault 1m, fault 2m, success sleeps the full interval and clears the streak,
        // so the next fault starts again from the 1m base (not 4m).
        worker
            .Waits.Should()
            .Equal(
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(2),
                sleep,
                TimeSpan.FromMinutes(1)
            );
    }

    [Fact]
    public async Task ManyConsecutiveFaults_StayPinnedAtCap_AndNeverOverflow()
    {
        var cap = TimeSpan.FromMilliseconds(100);
        var parked = new TaskCompletionSource();
        using var worker = new TestWorker(
            sleepInterval: TimeSpan.FromHours(1),
            errorBackoffInterval: TimeSpan.FromMilliseconds(1),
            maxErrorBackoffInterval: cap,
            doWork: (cycle, _, stoppingToken) =>
            {
                if (cycle <= 20)
                    throw new InvalidOperationException($"boom {cycle}");
                parked.TrySetResult();
                return Task.Delay(Timeout.Infinite, stoppingToken);
            }
        );

        await worker.StartAsync(CancellationToken.None);
        await parked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        // The doublings clamp keeps the shift from overflowing across a long fault streak:
        // every wait stays a positive value within the cap, and the tail pins at the cap.
        worker.Waits.Should().HaveCount(20);
        worker.Waits.Should().OnlyContain(wait => wait > TimeSpan.Zero && wait <= cap);
        worker.Waits[^1].Should().Be(cap);
    }

    // Records the interval the loop chose each cycle (via the WaitForNextCycle override) and
    // returns immediately, so the loop advances deterministically with no real waiting. The
    // final cycle parks on an infinite delay until StopAsync cancels it.
    private sealed class TestWorker : BaseScraperWorker
    {
        private readonly TimeSpan _sleep;
        private readonly TimeSpan _errorBackoff;
        private readonly TimeSpan _maxErrorBackoff;
        private readonly TimeSpan _continuation;
        private readonly Func<int, TestWorker, CancellationToken, Task> _doWork;
        private int _cycle;

        public List<TimeSpan> Waits { get; } = [];

        public TestWorker(
            TimeSpan sleepInterval,
            Func<int, TestWorker, CancellationToken, Task> doWork,
            TimeSpan? errorBackoffInterval = null,
            TimeSpan? maxErrorBackoffInterval = null,
            TimeSpan? continuationInterval = null
        )
            : base(
                NullLogger.Instance,
                Substitute.For<IServiceScopeFactory>(),
                new ErrorReporter(
                    Substitute.For<IServiceScopeFactory>(),
                    NullLogger<ErrorReporter>.Instance
                )
            )
        {
            _sleep = sleepInterval;
            _doWork = doWork;
            _errorBackoff = errorBackoffInterval ?? TimeSpan.FromMinutes(1);
            _maxErrorBackoff = maxErrorBackoffInterval ?? TimeSpan.FromMinutes(15);
            _continuation = continuationInterval ?? TimeSpan.FromSeconds(30);
        }

        protected override string WorkerName => "Test";
        protected override TimeSpan SleepInterval => _sleep;
        protected override TimeSpan ErrorBackoffInterval => _errorBackoff;
        protected override TimeSpan MaxErrorBackoffInterval => _maxErrorBackoff;
        protected override TimeSpan ContinuationInterval => _continuation;
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        public void CallRequestImmediateContinuation() => RequestImmediateContinuation();

        protected override Task DoWork(CancellationToken stoppingToken) =>
            _doWork(++_cycle, this, stoppingToken);

        protected override Task WaitForNextCycle(TimeSpan interval, CancellationToken stoppingToken)
        {
            Waits.Add(interval);
            return Task.CompletedTask;
        }
    }
}
