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

public class BaseScraperWorkerErrorBackoffActivityTests
{
    // A faulted cycle must publish a **Warn**-severity ScraperActivity whose message starts
    // with "cycle failed, retrying in ...", so the live activity feed shows the worker hit a
    // transient error and is backing off (not the default Info "cycle complete, sleeping ...").
    // A refactor that hardcoded Info here would silently hide repeated failures on the feed.
    [Fact]
    public async Task ExecuteAsync_FaultedCycle_PublishesWarnActivityWithCycleFailedMessage()
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

        using var worker = new FaultingTestWorker(
            Substitute.For<ILogger>(),
            scopeFactory,
            errorReporter,
            // Long intervals: we detect the publish then stop, so the worker is parked in
            // the backoff wait — no real waiting is observed.
            sleepInterval: TimeSpan.FromHours(1),
            errorBackoffInterval: TimeSpan.FromHours(1)
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() =>
            bus.ReceivedCalls()
                .Any(c =>
                    c.GetMethodInfo().Name == nameof(IBus.Publish)
                    && Captured(c).Message.StartsWith("cycle failed", StringComparison.Ordinal)
                )
        );
        await worker.StopAsync(CancellationToken.None);

        var cycleFailed = bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBus.Publish))
            .Select(Captured)
            .Where(a => a.Message.StartsWith("cycle failed", StringComparison.Ordinal))
            .ToList();

        cycleFailed
            .Should()
            .Contain(a =>
                a.Source == "FaultingTestWorker" && a.Severity == ScraperActivitySeverity.Warn
            );
    }

    private static ScraperActivity Captured(ICall call) => (ScraperActivity)call.GetArguments()[0]!;

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

    private sealed class FaultingTestWorker : BaseScraperWorker
    {
        protected override string WorkerName => "FaultingTestWorker";
        protected override TimeSpan SleepInterval { get; }
        protected override TimeSpan ErrorBackoffInterval { get; }
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        public FaultingTestWorker(
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            TimeSpan sleepInterval,
            TimeSpan errorBackoffInterval
        )
            : base(logger, scopeFactory, errorReporter)
        {
            SleepInterval = sleepInterval;
            ErrorBackoffInterval = errorBackoffInterval;
        }

        protected override Task DoWork(CancellationToken stoppingToken) =>
            throw new InvalidOperationException("boom");
    }
}
