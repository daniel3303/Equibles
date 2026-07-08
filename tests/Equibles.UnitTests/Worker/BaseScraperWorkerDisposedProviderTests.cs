#nullable enable

using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

public class BaseScraperWorkerDisposedProviderTests
{
    [Fact]
    public async Task ExecuteAsync_ScopeCreationThrowsObjectDisposed_WorkerCompletesWithoutFault()
    {
        // Repro for the shutdown noise seen in production: during host teardown
        // the root IServiceProvider is disposed, so PublishActivity's
        // CreateScope() throws ObjectDisposedException. That exception is not an
        // OperationCanceledException, so it escaped ExecuteAsync and made the
        // host log "BackgroundService failed" + a StopHost fatal for every
        // worker still in its cancellation path. The activity feed is
        // best-effort — scope-creation failures must be swallowed like any
        // other publish failure.
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns<IServiceScope>(_ =>
                throw new ObjectDisposedException(nameof(IServiceProvider))
            );

        var reporter = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());
        using var worker = new DisposedProviderTestWorker(
            Substitute.For<ILogger>(),
            scopeFactory,
            reporter
        );

        await worker.StartAsync(CancellationToken.None);
        // The "cycle started" publish already hits the throwing factory;
        // reaching DoWork proves the failure was swallowed instead of escaping.
        await WaitUntil(() => worker.DoWorkCalls >= 1);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCalls.Should().BeGreaterThanOrEqualTo(1);
        // Cancellation lands in DoWork, so the worker exits through its
        // OperationCanceledException handler — whose "cancelled" publish also
        // hits the throwing factory — and must still complete without fault.
        worker.ExecuteTask.Should().NotBeNull();
        worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
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

    private sealed class DisposedProviderTestWorker : BaseScraperWorker
    {
        protected override string WorkerName => "TestWorker";
        protected override TimeSpan SleepInterval => TimeSpan.FromSeconds(30);
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        public int DoWorkCalls;

        public DisposedProviderTestWorker(
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter
        )
            : base(logger, scopeFactory, errorReporter) { }

        protected override async Task DoWork(CancellationToken stoppingToken)
        {
            Interlocked.Increment(ref DoWorkCalls);
            // Block until shutdown so cancellation surfaces inside DoWork and
            // the worker exits through its cancelled path (the production
            // stack: cancelled → PublishActivity → CreateScope).
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}
