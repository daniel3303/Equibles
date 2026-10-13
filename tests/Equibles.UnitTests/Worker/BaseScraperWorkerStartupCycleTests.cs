#nullable enable
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

// A lane that opts in skips the cycle a restart would start while its last completed cycle is
// younger than SleepInterval, and records a completion only for a cycle that sleeps the full
// interval. Deploys restart the worker far more often than most lanes sleep.
public class BaseScraperWorkerStartupCycleTests
{
    private readonly ILogger _logger = Substitute.For<ILogger>();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ErrorReporter _errorReporter;

    public BaseScraperWorkerStartupCycleTests()
    {
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(Substitute.For<IServiceProvider>());
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _scopeFactory.CreateScope().Returns(scope);
        _errorReporter = new ErrorReporter(_scopeFactory, Substitute.For<ILogger<ErrorReporter>>());
    }

    [Fact]
    public async Task ExecuteAsync_FreshCompletion_SkipsTheStartupCycle()
    {
        using var worker = NewWorker(
            skip: true,
            completedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            sleepInterval: TimeSpan.FromHours(1)
        );

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(400);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().Be(0);
        worker.Recorded.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_StaleCompletion_RunsTheStartupCycleAndRecordsIt()
    {
        using var worker = NewWorker(
            skip: true,
            completedAt: DateTimeOffset.UtcNow.AddHours(-2),
            sleepInterval: TimeSpan.FromHours(1)
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => worker.Recorded != null);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().Be(1);
        worker.Recorded.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ExecuteAsync_NoCompletionYet_RunsTheStartupCycle()
    {
        using var worker = NewWorker(
            skip: true,
            completedAt: null,
            sleepInterval: TimeSpan.FromHours(1)
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => worker.DoWorkCalls >= 1);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ReadFault_RunsTheStartupCycle()
    {
        using var worker = NewWorker(
            skip: true,
            completedAt: DateTimeOffset.UtcNow,
            sleepInterval: TimeSpan.FromHours(1),
            readThrows: true
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => worker.DoWorkCalls >= 1);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_NotReadyRetry_IsNotRecordedAsACompletion()
    {
        using var worker = NewWorker(
            skip: true,
            completedAt: null,
            sleepInterval: TimeSpan.FromHours(1),
            doWork: w =>
            {
                w.CallRequestRetrySoon();
                return Task.CompletedTask;
            }
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => worker.DoWorkCalls >= 1);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().Be(1);
        worker.Recorded.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_QueuedContinuation_IsNotRecordedAsACompletion()
    {
        using var worker = NewWorker(
            skip: true,
            completedAt: null,
            sleepInterval: TimeSpan.FromHours(1),
            doWork: w =>
            {
                w.CallRequestImmediateContinuation();
                return Task.CompletedTask;
            }
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => worker.DoWorkCalls >= 1);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().Be(1);
        worker.Recorded.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_FaultedCycle_IsNotRecordedAsACompletion()
    {
        using var worker = NewWorker(
            skip: true,
            completedAt: null,
            sleepInterval: TimeSpan.FromHours(1),
            doWork: _ => throw new InvalidOperationException("boom")
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => worker.DoWorkCalls >= 1);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().Be(1);
        worker.Recorded.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WriteFault_KeepsTheLaneCycling()
    {
        using var worker = NewWorker(
            skip: true,
            completedAt: null,
            sleepInterval: TimeSpan.FromMilliseconds(20),
            writeThrows: true
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => worker.DoWorkCalls >= 2);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().BeGreaterThanOrEqualTo(2, "a stamp fault never parks the lane");
        worker.Recorded.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NotOptedIn_NeverReadsOrRecordsACompletion()
    {
        using var worker = NewWorker(
            skip: false,
            completedAt: DateTimeOffset.UtcNow,
            sleepInterval: TimeSpan.FromHours(1)
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => worker.DoWorkCalls >= 1);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().Be(1);
        worker.ReadCalls.Should().Be(0);
        worker.Recorded.Should().BeNull();
    }

    private StartupCycleTestWorker NewWorker(
        bool skip,
        DateTimeOffset? completedAt,
        TimeSpan sleepInterval,
        bool readThrows = false,
        bool writeThrows = false,
        Func<StartupCycleTestWorker, Task>? doWork = null
    ) =>
        new(
            _logger,
            _scopeFactory,
            _errorReporter,
            sleepInterval,
            skip,
            completedAt,
            readThrows,
            writeThrows,
            doWork ?? (_ => Task.CompletedTask)
        );

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(10);
        }
    }

    private sealed class StartupCycleTestWorker : BaseScraperWorker
    {
        private readonly bool _skip;
        private readonly DateTimeOffset? _completedAt;
        private readonly bool _readThrows;
        private readonly bool _writeThrows;
        private readonly Func<StartupCycleTestWorker, Task> _doWork;

        protected override string WorkerName => "StartupCycleTestWorker";
        protected override TimeSpan SleepInterval { get; }
        protected override ErrorSource ErrorSource => ErrorSource.Other;
        protected override bool SkipStartupCycleWhenFresh => _skip;

        public int DoWorkCalls;
        public int ReadCalls;
        public DateTimeOffset? Recorded;

        public StartupCycleTestWorker(
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            TimeSpan sleepInterval,
            bool skip,
            DateTimeOffset? completedAt,
            bool readThrows,
            bool writeThrows,
            Func<StartupCycleTestWorker, Task> doWork
        )
            : base(logger, scopeFactory, errorReporter)
        {
            SleepInterval = sleepInterval;
            _skip = skip;
            _completedAt = completedAt;
            _readThrows = readThrows;
            _writeThrows = writeThrows;
            _doWork = doWork;
        }

        public void CallRequestRetrySoon() => RequestRetrySoon();

        public void CallRequestImmediateContinuation() => RequestImmediateContinuation();

        protected override Task DoWork(CancellationToken stoppingToken)
        {
            Interlocked.Increment(ref DoWorkCalls);
            return _doWork(this);
        }

        protected override Task<DateTimeOffset?> ReadLastCycleCompletion(
            CancellationToken stoppingToken
        )
        {
            Interlocked.Increment(ref ReadCalls);
            if (_readThrows)
                throw new InvalidOperationException("stamp store unavailable");
            return Task.FromResult(_completedAt);
        }

        protected override Task WriteCycleCompletion(
            DateTimeOffset completedAt,
            CancellationToken stoppingToken
        )
        {
            if (_writeThrows)
                throw new InvalidOperationException("stamp store unavailable");
            Recorded = completedAt;
            return Task.CompletedTask;
        }
    }
}
