#nullable enable

using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Messaging.Contracts.Activity;
using Equibles.Worker;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;

namespace Equibles.UnitTests.Worker;

public class BaseScraperWorkerActivityTests
{
    private readonly ILogger _logger = Substitute.For<ILogger>();
    private readonly IBus _bus = Substitute.For<IBus>();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ErrorReporter _errorReporter;

    public BaseScraperWorkerActivityTests()
    {
        // Wire a scope factory that hands back an IBus, so BaseScraperWorker's
        // lazy resolution finds the substitute. Each scope gets the SAME bus
        // mock so a single .Received() on the bus covers calls from any scope.
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBus)).Returns(_bus);
        scope.ServiceProvider.Returns(serviceProvider);
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _scopeFactory.CreateScope().Returns(scope);

        _errorReporter = new ErrorReporter(_scopeFactory, Substitute.For<ILogger<ErrorReporter>>());
    }

    [Fact]
    public async Task ExecuteAsync_NormalCycle_PublishesStartAndCompleteActivities()
    {
        using var worker = new ActivityTestWorker(
            _logger,
            _scopeFactory,
            _errorReporter,
            sleepInterval: TimeSpan.FromSeconds(30)
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => worker.DoWorkCalls >= 1);
        // Wait for the post-cycle publish too — DoWorkCalls flips inside DoWork
        // but the "cycle complete" publish happens after DoWork returns.
        await WaitUntil(() =>
            _bus.ReceivedCalls()
                .Any(c => Captured(c).Message.Contains("cycle complete", StringComparison.Ordinal))
        );
        await worker.StopAsync(CancellationToken.None);

        var published = _bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBus.Publish))
            .Select(Captured)
            .ToList();

        published
            .Should()
            .Contain(a =>
                a.Source == "TestWorker"
                && a.Severity == ScraperActivitySeverity.Info
                && a.Message == "cycle started"
            );
        published
            .Should()
            .Contain(a =>
                a.Source == "TestWorker"
                && a.Severity == ScraperActivitySeverity.Info
                && a.Message.StartsWith("cycle complete", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task ExecuteAsync_NoBusRegistered_CycleStillRunsToCompletion()
    {
        // Hosts that wire BaseScraperWorker without messaging (legacy code paths,
        // future micro-host slices) must keep working — publish lookups fall
        // back to no-op, so DoWork still runs and the cycle still loops.
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBus)).Returns((object?)null);
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var reporter = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());
        using var worker = new ActivityTestWorker(
            _logger,
            scopeFactory,
            reporter,
            sleepInterval: TimeSpan.FromSeconds(30)
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => worker.DoWorkCalls >= 1);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_BusPublishThrows_CycleStillCompletes()
    {
        // Broker errors must never crash the scraper. If Publish throws, the
        // worker should still progress through DoWork and the sleep block.
        _bus.Publish(Arg.Any<ScraperActivity>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("broker down")));

        using var worker = new ActivityTestWorker(
            _logger,
            _scopeFactory,
            _errorReporter,
            sleepInterval: TimeSpan.FromSeconds(30)
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => worker.DoWorkCalls >= 1);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_DoWorkThrows_ErrorReporterPublishesErrorSeverityActivity()
    {
        var failure = new InvalidOperationException("boom");
        using var worker = new ActivityTestWorker(
            _logger,
            _scopeFactory,
            _errorReporter,
            sleepInterval: TimeSpan.FromSeconds(30),
            doWorkOverride: _ => throw failure
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() =>
            _bus.ReceivedCalls().Any(c => Captured(c).Severity == ScraperActivitySeverity.Error)
        );
        await worker.StopAsync(CancellationToken.None);

        var errors = _bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBus.Publish))
            .Select(Captured)
            .Where(a => a.Severity == ScraperActivitySeverity.Error)
            .ToList();

        errors
            .Should()
            .Contain(a => a.Source == ErrorSource.Other.Value && a.Message.Contains("boom"));
    }

    private static ScraperActivity Captured(ICall call)
    {
        var args = call.GetArguments();
        return (ScraperActivity)args[0]!;
    }

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

    private sealed class ActivityTestWorker : BaseScraperWorker
    {
        private readonly Func<CancellationToken, Task>? _doWorkOverride;

        protected override string WorkerName => "TestWorker";
        protected override TimeSpan SleepInterval { get; }
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        public int DoWorkCalls;

        public ActivityTestWorker(
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            TimeSpan sleepInterval,
            Func<CancellationToken, Task>? doWorkOverride = null
        )
            : base(logger, scopeFactory, errorReporter)
        {
            SleepInterval = sleepInterval;
            _doWorkOverride = doWorkOverride;
        }

        protected override async Task DoWork(CancellationToken stoppingToken)
        {
            Interlocked.Increment(ref DoWorkCalls);
            if (_doWorkOverride is not null)
                await _doWorkOverride(stoppingToken);
        }
    }
}
