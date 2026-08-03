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
        private int _leaseAttempts;
        private int _doWorkCalls;

        public LeaseTestWorker(
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            Func<int, CancellationToken, ValueTask<IAsyncDisposable>> tryAcquire,
            Func<CancellationToken, Task> doWork
        )
            : base(logger, scopeFactory, errorReporter)
        {
            _tryAcquire = tryAcquire;
            _doWork = doWork;
        }

        public Task<TimeSpan> FirstWait => _firstWait.Task;
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
            return Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
    }
}
