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

public class BaseScraperWorkerRetrySoonActivityTests
{
    // Per #1576: a cycle that called RequestRetrySoon must publish a
    // **Warn**-severity ScraperActivity with message starting with
    // "not ready, retrying in ...", instead of the default Info "cycle
    // complete, sleeping ...". A refactor that flipped the ternary to a
    // hardcoded Info severity (e.g. while "cleaning up" the post-cycle
    // log/publish block) would silently demote cold-start signals on the
    // live activity feed.
    [Fact]
    public async Task ExecuteAsync_RequestRetrySoon_PublishesWarnActivityWithNotReadyMessage()
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

        using var worker = new RetrySoonTestWorker(
            Substitute.For<ILogger>(),
            scopeFactory,
            errorReporter,
            sleepInterval: TimeSpan.FromHours(1),
            retryInterval: TimeSpan.FromHours(1)
        );

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() =>
            bus.ReceivedCalls()
                .Any(c =>
                    c.GetMethodInfo().Name == nameof(IBus.Publish)
                    && Captured(c).Message.StartsWith("not ready", StringComparison.Ordinal)
                )
        );
        await worker.StopAsync(CancellationToken.None);

        var notReady = bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBus.Publish))
            .Select(Captured)
            .Where(a => a.Message.StartsWith("not ready", StringComparison.Ordinal))
            .ToList();

        notReady
            .Should()
            .Contain(a =>
                a.Source == "RetrySoonTestWorker" && a.Severity == ScraperActivitySeverity.Warn
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

    private sealed class RetrySoonTestWorker : BaseScraperWorker
    {
        protected override string WorkerName => "RetrySoonTestWorker";
        protected override TimeSpan SleepInterval { get; }
        protected override TimeSpan NotReadyRetryInterval { get; }
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        public RetrySoonTestWorker(
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            TimeSpan sleepInterval,
            TimeSpan retryInterval
        )
            : base(logger, scopeFactory, errorReporter)
        {
            SleepInterval = sleepInterval;
            NotReadyRetryInterval = retryInterval;
        }

        protected override Task DoWork(CancellationToken stoppingToken)
        {
            RequestRetrySoon();
            return Task.CompletedTask;
        }
    }
}
