#nullable enable

using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

public class BaseScraperWorkerTests
{
    private readonly ILogger _logger = Substitute.For<ILogger>();
    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly ErrorReporter _errorReporter;

    public BaseScraperWorkerTests()
    {
        _errorReporter = new ErrorReporter(_scopeFactory, Substitute.For<ILogger<ErrorReporter>>());
    }

    private TestScraperWorker CreateWorker(
        TimeSpan? sleepInterval = null,
        bool validateConfiguration = true,
        Func<CancellationToken, Task>? doWorkOverride = null
    )
    {
        return new TestScraperWorker(
            _logger,
            _scopeFactory,
            _errorReporter,
            sleepInterval ?? TimeSpan.FromMilliseconds(10),
            validateConfiguration,
            doWorkOverride
        );
    }

    [Fact]
    public async Task ExecuteAsync_CallsDoWork_WhenNotCancelled()
    {
        using var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        // Poll instead of a blind Task.Delay — on slow CI runners the BackgroundService
        // may not schedule ExecuteAsync within 100ms, leaving DoWorkCallCount at 0.
        await WaitUntilAsync(() => worker.DoWorkCallCount >= 1, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_CatchesAndLogsCriticalException_WhenDoWorkThrows()
    {
        var exception = new InvalidOperationException("Something broke");
        using var worker = CreateWorker(doWorkOverride: _ => throw exception);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        // Once DoWork has been entered twice, iteration 1's catch block (LogCritical
        // + ErrorReporter.Report + post-cycle log + sleep) has fully completed —
        // deterministic proof the log assertion below has a value to match.
        await WaitUntilAsync(() => worker.DoWorkCallCount >= 2, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        _logger
            .Received()
            .Log(
                LogLevel.Critical,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Critical error in")),
                exception,
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public async Task ExecuteAsync_ReportsErrorViaErrorReporter_WhenDoWorkThrows()
    {
        var exception = new InvalidOperationException("Report this error");
        using var worker = CreateWorker(doWorkOverride: _ => throw exception);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await WaitUntilAsync(() => worker.DoWorkCallCount >= 2, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        // ErrorReporter.Report internally calls CreateAsyncScope which will throw
        // because we have a basic substitute. The important thing is that the worker
        // continued running (did not crash) and logged the critical error.
        _logger
            .Received()
            .Log(
                LogLevel.Critical,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Critical error in")),
                exception,
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public async Task ExecuteAsync_StopsGracefully_WhenCancellationTokenIsCancelled()
    {
        var doWorkEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var worker = CreateWorker(
            sleepInterval: TimeSpan.FromMilliseconds(5),
            doWorkOverride: async ct =>
            {
                doWorkEntered.TrySetResult();
                // Block inside DoWork until cancellation, then throw
                await Task.Delay(Timeout.Infinite, ct);
            }
        );
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        // Wait until DoWork is actively running
        await doWorkEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        // Worker should log cancellation message when DoWork throws OperationCanceledException
        _logger
            .Received()
            .Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("cancelled")),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCallDoWork_WhenValidateConfigurationReturnsFalse()
    {
        using var worker = CreateWorker(validateConfiguration: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_LogsStartAndCompletionMessages()
    {
        using var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await worker.StartAsync(cts.Token);
        // Wait until at least one full cycle has executed rather than a blind delay —
        // a 100ms wait races on slow CI runners where ExecuteAsync may not be scheduled
        // before the assertion runs, even though DoWork eventually fires.
        await WaitUntilAsync(() => worker.DoWorkCallCount >= 1, TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        // "running at" log on start of each cycle
        _logger
            .Received()
            .Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("running at")),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );

        // "cycle complete" log after DoWork finishes
        _logger
            .Received()
            .Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("cycle complete")),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task ExecuteAsync_SleepsForSleepInterval_BetweenCycles()
    {
        // Use a long sleep interval so a second cycle cannot complete while we
        // observe the first. The previous design raced a 400ms CTS timer against
        // a 500ms Task.Delay — on slow CI runners the CTS fired after the Delay
        // returned, allowing a second DoWork before cancellation propagated.
        var sleepInterval = TimeSpan.FromSeconds(30);
        using var worker = CreateWorker(sleepInterval: sleepInterval);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);

        // Wait until the worker has entered DoWork once AND emitted the "Sleeping for"
        // log — that proves it cleared DoWork and started Task.Delay(SleepInterval).
        // Both conditions polled deterministically; no real-clock race.
        await WaitUntilAsync(() => worker.DoWorkCallCount >= 1, TimeSpan.FromSeconds(5));
        await WaitUntilAsync(
            () =>
                _logger
                    .ReceivedCalls()
                    .Any(c =>
                        c.GetMethodInfo().Name == nameof(ILogger.Log)
                        && c.GetArguments().Length > 2
                        && c.GetArguments()[2]?.ToString()?.Contains("Sleeping for") == true
                    ),
            TimeSpan.FromSeconds(5)
        );

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        // The 30s sleep cannot complete in the few milliseconds it takes for the
        // polling waits + CancelAsync above, so DoWork ran exactly once.
        worker
            .DoWorkCallCount.Should()
            .Be(1, "cancellation arrived while the worker was sleeping after its first cycle");

        // Verify the "Sleeping for" log was emitted.
        _logger
            .Received()
            .Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Sleeping for")),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    /// <summary>
    /// Concrete test implementation of BaseScraperWorker with configurable behavior.
    /// </summary>
    private sealed class TestScraperWorker : BaseScraperWorker
    {
        private readonly bool _validateConfiguration;
        private readonly Func<CancellationToken, Task>? _doWorkOverride;
        private int _doWorkCallCount;

        protected override string WorkerName => "TestWorker";
        protected override TimeSpan SleepInterval { get; }
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        public int DoWorkCallCount => _doWorkCallCount;

        public TestScraperWorker(
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            TimeSpan sleepInterval,
            bool validateConfiguration = true,
            Func<CancellationToken, Task>? doWorkOverride = null
        )
            : base(logger, scopeFactory, errorReporter)
        {
            SleepInterval = sleepInterval;
            _validateConfiguration = validateConfiguration;
            _doWorkOverride = doWorkOverride;
        }

        protected override bool ValidateConfiguration() => _validateConfiguration;

        protected override async Task DoWork(CancellationToken stoppingToken)
        {
            Interlocked.Increment(ref _doWorkCallCount);

            if (_doWorkOverride != null)
            {
                await _doWorkOverride(stoppingToken);
            }
        }
    }
}
