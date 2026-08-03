#nullable enable

using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Messaging.Contracts.Activity;
using Equibles.Worker;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

public class BaseScraperWorkerLaneLeaseTests
{
    [Fact]
    public async Task ExecuteAsync_LeaseHeldElsewhere_SkipsWorkWithoutFaultOrError()
    {
        var bus = Substitute.For<IBus>();
        using var services = CreateWorkerServices(new WorkerOptions(), bus);
        var reporterScopeFactory = Substitute.For<IServiceScopeFactory>();
        var logger = Substitute.For<ILogger>();
        using var worker = new LeaseTestWorker(
            logger,
            services.GetRequiredService<IServiceScopeFactory>(),
            CreateErrorReporter(reporterScopeFactory),
            (_, _) => ValueTask.FromResult<IAsyncDisposable>(null!),
            _ => Task.CompletedTask
        );

        await worker.StartAsync(CancellationToken.None);
        var interval = await worker.FirstWait.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        worker.LeaseAttempts.Should().Be(1);
        worker.DoWorkCalls.Should().Be(0);
        interval.Should().Be(worker.NormalSleepInterval);
        reporterScopeFactory.DidNotReceive().CreateScope();
        logger
            .DidNotReceive()
            .Log(
                LogLevel.Critical,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
        bus.ReceivedCalls()
            .Select(call => call.GetArguments().FirstOrDefault())
            .OfType<ScraperActivity>()
            .Should()
            .NotContain(activity =>
                activity.Severity == ScraperActivitySeverity.Warn
                || activity.Severity == ScraperActivitySeverity.Error
            );
    }

    [Fact]
    public async Task ExecuteAsync_LeaseFree_RunsWorkNormally()
    {
        var lease = new TrackingLease();
        using var services = CreateWorkerServices(new WorkerOptions());
        using var worker = new LeaseTestWorker(
            Substitute.For<ILogger>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            CreateErrorReporter(Substitute.For<IServiceScopeFactory>()),
            (_, _) => ValueTask.FromResult<IAsyncDisposable>(lease),
            _ => Task.CompletedTask
        );

        await worker.StartAsync(CancellationToken.None);
        var interval = await worker.FirstWait.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        new WorkerOptions().LaneLeaseEnabled.Should().BeTrue();
        worker.LeaseAttempts.Should().Be(1);
        worker.DoWorkCalls.Should().Be(1);
        interval.Should().Be(worker.NormalSleepInterval);
        lease.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_DoWorkThrows_ReleasesLeaseBeforeFaultBackoff()
    {
        var lease = new TrackingLease();
        using var services = CreateWorkerServices(new WorkerOptions());
        using var worker = new LeaseTestWorker(
            Substitute.For<ILogger>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            CreateErrorReporter(Substitute.For<IServiceScopeFactory>()),
            (_, _) => ValueTask.FromResult<IAsyncDisposable>(lease),
            _ => throw new InvalidOperationException("cycle failed")
        );

        await worker.StartAsync(CancellationToken.None);
        var interval = await worker.FirstWait.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().Be(1);
        lease.DisposeCalls.Should().Be(1);
        interval.Should().Be(worker.FaultBackoffInterval);
    }

    [Fact]
    public async Task ExecuteAsync_LaneLeaseDisabled_RunsWorkWithoutAttemptingLease()
    {
        using var services = CreateWorkerServices(new WorkerOptions { LaneLeaseEnabled = false });
        using var worker = new LeaseTestWorker(
            Substitute.For<ILogger>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            CreateErrorReporter(Substitute.For<IServiceScopeFactory>()),
            (_, _) => throw new InvalidOperationException("lease must not be attempted"),
            _ => Task.CompletedTask
        );

        await worker.StartAsync(CancellationToken.None);
        var interval = await worker.FirstWait.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        worker.LeaseAttempts.Should().Be(0);
        worker.DoWorkCalls.Should().Be(1);
        interval.Should().Be(worker.NormalSleepInterval);
    }

    [Fact]
    public async Task ExecuteAsync_LeaseLostMidOutage_DoesNotResetTheFailureStreak()
    {
        // A skipped cycle must not read as a CLEAN one. The clean-cycle branch clears
        // _consecutiveFailures and _errorReportedForStreak, so a worker deep in an outage that
        // loses the lease for a single cycle would have its streak reset and its Error report
        // pushed further away — the report exists precisely to surface an outage that is not
        // self-healing. With ErrorReportThreshold = 2: fault, lose the lease, fault again must
        // still reach the threshold and report.
        var reporterScopeFactory = Substitute.For<IServiceScopeFactory>();
        using var services = CreateWorkerServices(new WorkerOptions());
        using var worker = new LeaseTestWorker(
            Substitute.For<ILogger>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            CreateErrorReporter(reporterScopeFactory),
            // Only the second cycle loses the lane.
            (attempt, _) =>
                ValueTask.FromResult<IAsyncDisposable>(attempt == 2 ? null! : new TrackingLease()),
            _ => throw new InvalidOperationException("cycle failed"),
            cyclesBeforeBlocking: 3
        );

        await worker.StartAsync(CancellationToken.None);
        await worker.CyclesCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().Be(2, "the middle cycle never acquired the lane");
        reporterScopeFactory.Received().CreateScope();
    }

    [Fact]
    public void LaneId_IsTheWorkerType_NotTheMutableDisplayName()
    {
        // WorkerName is a display string for logs and the activity feed; rewording it must not
        // move the worker to a different advisory lock. If it did, a rolling release that renamed
        // a worker would leave old and new pods holding two locks for ONE logical lane and running
        // it concurrently — precisely the overlap the lease exists to prevent, reintroduced by an
        // edit that looks cosmetic. The lane identity is therefore the concrete type.
        using var services = CreateWorkerServices(new WorkerOptions());
        using var worker = new LeaseTestWorker(
            Substitute.For<ILogger>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            CreateErrorReporter(Substitute.For<IServiceScopeFactory>()),
            (_, _) => ValueTask.FromResult<IAsyncDisposable>(new TrackingLease()),
            _ => Task.CompletedTask
        );

        worker.ExposedLaneId.Should().Be(typeof(LeaseTestWorker).FullName);
        worker.ExposedLaneId.Should().NotBe(worker.ExposedWorkerName);
    }

    private static ServiceProvider CreateWorkerServices(WorkerOptions options, IBus? bus = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<WorkerOptions>>(Options.Create(options));
        if (bus is not null)
            services.AddSingleton(bus);
        return services.BuildServiceProvider();
    }

    private static ErrorReporter CreateErrorReporter(IServiceScopeFactory scopeFactory) =>
        new(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

    private sealed class TrackingLease : IAsyncDisposable
    {
        public int DisposeCalls { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LeaseTestWorker : BaseScraperWorker
    {
        private readonly Func<int, CancellationToken, ValueTask<IAsyncDisposable>> _tryAcquire;
        private readonly Func<CancellationToken, Task> _doWork;
        private readonly TaskCompletionSource<TimeSpan> _firstWait = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _cyclesCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly int _cyclesBeforeBlocking;
        private int _leaseAttempts;
        private int _doWorkCalls;
        private int _cyclesObserved;

        public LeaseTestWorker(
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            Func<int, CancellationToken, ValueTask<IAsyncDisposable>> tryAcquire,
            Func<CancellationToken, Task> doWork,
            int cyclesBeforeBlocking = 1
        )
            : base(logger, scopeFactory, errorReporter)
        {
            _tryAcquire = tryAcquire;
            _doWork = doWork;
            _cyclesBeforeBlocking = cyclesBeforeBlocking;
        }

        public Task<TimeSpan> FirstWait => _firstWait.Task;
        public Task CyclesCompleted => _cyclesCompleted.Task;
        public string ExposedLaneId => LaneId;
        public string ExposedWorkerName => WorkerName;
        public TimeSpan NormalSleepInterval => SleepInterval;
        public TimeSpan FaultBackoffInterval => ErrorBackoffInterval;
        public int LeaseAttempts => _leaseAttempts;
        public int DoWorkCalls => _doWorkCalls;

        protected override string WorkerName => "Lease test worker";
        protected override TimeSpan SleepInterval => TimeSpan.FromHours(1);
        protected override TimeSpan ErrorBackoffInterval => TimeSpan.FromSeconds(1);
        protected override int ErrorReportThreshold => 2;
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        protected override ValueTask<IAsyncDisposable> TryAcquireLaneLease(
            CancellationToken stoppingToken
        ) => _tryAcquire(Interlocked.Increment(ref _leaseAttempts), stoppingToken);

        protected override Task DoWork(CancellationToken stoppingToken)
        {
            Interlocked.Increment(ref _doWorkCalls);
            return _doWork(stoppingToken);
        }

        protected override Task WaitForNextCycle(TimeSpan interval, CancellationToken stoppingToken)
        {
            _firstWait.TrySetResult(interval);

            // Let the loop run the requested number of cycles back to back, then park so the
            // test observes a settled state instead of racing an unbounded loop.
            if (Interlocked.Increment(ref _cyclesObserved) < _cyclesBeforeBlocking)
                return Task.CompletedTask;

            _cyclesCompleted.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
    }
}
