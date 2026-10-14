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

// A per-item catch filtered on `!stoppingToken.IsCancellationRequested` stands down once the
// host stops, so an ordinary item outcome thrown mid-teardown escapes DoWork as a non-cancellation
// exception. The work retries on the next run, so it must stay a warning, never an Errors row.
public class BaseScraperWorkerShutdownFaultTests
{
    [Fact]
    public async Task FaultEscapingAfterStopSignal_IsAWarningNotAReport()
    {
        var bus = Substitute.For<IBus>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBus)).Returns(bus);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        var logger = Substitute.For<ILogger>();
        var parked = new TaskCompletionSource();
        using var worker = new ShutdownFaultTestWorker(logger, scopeFactory, parked);

        await worker.StartAsync(CancellationToken.None);
        await parked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        logger
            .DidNotReceive()
            .Log(
                LogLevel.Critical,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
        logger
            .Received(1)
            .Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Is<Exception?>(e => e is InvalidOperationException),
                Arg.Any<Func<object, Exception?, string>>()
            );
        bus.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IBus.Publish))
            .Select(call => (ScraperActivity)call.GetArguments()[0]!)
            .Should()
            .Contain(activity =>
                activity.Severity == ScraperActivitySeverity.Warn && activity.Message == "cancelled"
            );
    }

    private sealed class ShutdownFaultTestWorker : BaseScraperWorker
    {
        private readonly TaskCompletionSource _parked;

        public ShutdownFaultTestWorker(
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            TaskCompletionSource parked
        )
            : base(
                logger,
                scopeFactory,
                new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>())
            )
        {
            _parked = parked;
        }

        protected override string WorkerName => "Shutdown fault test";
        protected override TimeSpan SleepInterval => TimeSpan.FromSeconds(30);
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        protected override async Task DoWork(CancellationToken stoppingToken)
        {
            _parked.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("item outcome thrown during teardown");
            }
        }
    }
}
