using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

public class BaseScraperWorkerRetrySoonTests
{
    // GH-851: a cycle that calls RequestRetrySoon (e.g. cold start, tracked
    // universe not populated yet) must wait the short NotReadyRetryInterval,
    // NOT the full SleepInterval — so a second cycle happens within a moment.
    // SleepInterval is set to 1h here: if the retry-soon path didn't apply, the
    // second cycle would never arrive and the test would time out.
    [Fact]
    public async Task RequestRetrySoon_UsesShortInterval_SoNextCycleRunsPromptly()
    {
        var secondCycle = new TaskCompletionSource();
        var cycles = 0;
        var sut = new TestWorker(
            sleepInterval: TimeSpan.FromHours(1),
            retryInterval: TimeSpan.FromMilliseconds(20),
            doWork: (worker, _) =>
            {
                cycles++;
                if (cycles == 1)
                {
                    worker.CallRequestRetrySoon();
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
            .Be(secondCycle.Task, "the retry-soon interval should trigger a prompt second cycle");
        cycles.Should().BeGreaterThanOrEqualTo(2);
    }

    private sealed class TestWorker : BaseScraperWorker
    {
        private readonly TimeSpan _sleep;
        private readonly TimeSpan _retry;
        private readonly Func<TestWorker, CancellationToken, Task> _doWork;

        public TestWorker(
            TimeSpan sleepInterval,
            TimeSpan retryInterval,
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
            _doWork = doWork;
        }

        protected override string WorkerName => "Test";
        protected override TimeSpan SleepInterval => _sleep;
        protected override TimeSpan NotReadyRetryInterval => _retry;
        protected override ErrorSource ErrorSource => ErrorSource.FtdScraper;

        public void CallRequestRetrySoon() => RequestRetrySoon();

        protected override Task DoWork(CancellationToken stoppingToken) =>
            _doWork(this, stoppingToken);
    }
}
