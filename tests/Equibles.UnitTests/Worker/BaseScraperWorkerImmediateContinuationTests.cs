using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

public class BaseScraperWorkerImmediateContinuationTests
{
    // A cycle that calls RequestImmediateContinuation (e.g. an as-filed HTML backfill that
    // filled its batch and still has a backlog queued) must wait the short ContinuationInterval,
    // NOT the full SleepInterval — so the backlog drains in successive bursts instead of one
    // batch every SleepInterval. SleepInterval is 1h here: if the continuation path didn't
    // apply, the second cycle would never arrive and the test would time out.
    [Fact]
    public async Task RequestImmediateContinuation_UsesShortInterval_SoNextCycleRunsPromptly()
    {
        var secondCycle = new TaskCompletionSource();
        var cycles = 0;
        var sut = new TestWorker(
            sleepInterval: TimeSpan.FromHours(1),
            retryInterval: TimeSpan.FromHours(1),
            continuationInterval: TimeSpan.FromMilliseconds(20),
            doWork: (worker, _) =>
            {
                cycles++;
                if (cycles == 1)
                {
                    worker.CallRequestImmediateContinuation();
                }
                else
                {
                    secondCycle.TrySetResult();
                }
                return Task.CompletedTask;
            }
        );

        await sut.StartAsync(CancellationToken.None);
        var reached = await Task.WhenAny(secondCycle.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await sut.StopAsync(CancellationToken.None);

        reached
            .Should()
            .Be(secondCycle.Task, "the continuation interval should trigger a prompt second cycle");
        cycles.Should().BeGreaterThanOrEqualTo(2);
    }

    // A not-yet-ready dependency is a reason to back off, not to press on: when a cycle
    // signals BOTH retry-soon and immediate-continuation, retry-soon must win. Here the
    // retry interval is short (20ms) and the continuation interval is 1h, so a prompt second
    // cycle proves the retry path was chosen; if continuation had won the test would time out.
    [Fact]
    public async Task RequestRetrySoon_OutranksImmediateContinuation()
    {
        var secondCycle = new TaskCompletionSource();
        var cycles = 0;
        var sut = new TestWorker(
            sleepInterval: TimeSpan.FromHours(1),
            retryInterval: TimeSpan.FromMilliseconds(20),
            continuationInterval: TimeSpan.FromHours(1),
            doWork: (worker, _) =>
            {
                cycles++;
                if (cycles == 1)
                {
                    worker.CallRequestRetrySoon();
                    worker.CallRequestImmediateContinuation();
                }
                else
                {
                    secondCycle.TrySetResult();
                }
                return Task.CompletedTask;
            }
        );

        await sut.StartAsync(CancellationToken.None);
        var reached = await Task.WhenAny(secondCycle.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await sut.StopAsync(CancellationToken.None);

        reached.Should().Be(secondCycle.Task, "retry-soon must outrank immediate-continuation");
        cycles.Should().BeGreaterThanOrEqualTo(2);
    }

    private sealed class TestWorker : BaseScraperWorker
    {
        private readonly TimeSpan _sleep;
        private readonly TimeSpan _retry;
        private readonly TimeSpan _continuation;
        private readonly Func<TestWorker, CancellationToken, Task> _doWork;

        public TestWorker(
            TimeSpan sleepInterval,
            TimeSpan retryInterval,
            TimeSpan continuationInterval,
            Func<TestWorker, CancellationToken, Task> doWork
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
            _retry = retryInterval;
            _continuation = continuationInterval;
            _doWork = doWork;
        }

        protected override string WorkerName => "Test";
        protected override TimeSpan SleepInterval => _sleep;
        protected override TimeSpan NotReadyRetryInterval => _retry;
        protected override TimeSpan ContinuationInterval => _continuation;
        protected override ErrorSource ErrorSource => ErrorSource.FtdScraper;

        public void CallRequestRetrySoon() => RequestRetrySoon();

        public void CallRequestImmediateContinuation() => RequestImmediateContinuation();

        protected override Task DoWork(CancellationToken stoppingToken) =>
            _doWork(this, stoppingToken);
    }
}
