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

public class BaseScraperWorkerCancelledActivityTests
{
    // Per #1576: when a scraper cycle is cancelled mid-flight (StopAsync
    // during DoWork), BaseScraperWorker's OperationCanceledException catch
    // must publish a Warn-severity ScraperActivity with message "cancelled"
    // — using CancellationToken.None, so the bus call survives the
    // stoppingToken having already been cancelled. The existing activity
    // tests cover the normal "cycle started" / "cycle complete" path and
    // the DoWork-throws path, but NOT the cancel path. A refactor that
    // dropped the publish, downgraded its severity, or wired stoppingToken
    // into the publish (which would no-op because it's already cancelled)
    // would compile and silently strip shutdown signals from the live feed.
    [Fact]
    public async Task ExecuteAsync_CancelledMidCycle_PublishesWarnCancelledActivity()
    {
        var bus = Substitute.For<IBus>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBus)).Returns(bus);
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        var errorReporter = new ErrorReporter(
            scopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var doWorkEntered = new TaskCompletionSource();
        using var worker = new CancelMidCycleTestWorker(
            Substitute.For<ILogger>(),
            scopeFactory,
            errorReporter,
            sleepInterval: TimeSpan.FromHours(1),
            onEnter: () => doWorkEntered.TrySetResult()
        );

        await worker.StartAsync(CancellationToken.None);
        await doWorkEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var published = bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBus.Publish))
            .Select(Captured)
            .ToList();

        published
            .Should()
            .Contain(a =>
                a.Source == "CancelMidCycleTestWorker"
                && a.Severity == ScraperActivitySeverity.Warn
                && a.Message == "cancelled"
            );
    }

    private static ScraperActivity Captured(ICall call) => (ScraperActivity)call.GetArguments()[0]!;

    private sealed class CancelMidCycleTestWorker : BaseScraperWorker
    {
        private readonly Action _onEnter;

        protected override string WorkerName => "CancelMidCycleTestWorker";
        protected override TimeSpan SleepInterval { get; }
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        public CancelMidCycleTestWorker(
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            TimeSpan sleepInterval,
            Action onEnter
        )
            : base(logger, scopeFactory, errorReporter)
        {
            SleepInterval = sleepInterval;
            _onEnter = onEnter;
        }

        protected override async Task DoWork(CancellationToken stoppingToken)
        {
            _onEnter();
            // Park inside DoWork until StopAsync cancels — this is the only
            // way to drive the OperationCanceledException path in the
            // ExecuteAsync catch block.
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
