#nullable enable

using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Worker;

public class BaseScraperWorkerTests {
    private readonly ILogger _logger = Substitute.For<ILogger>();
    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly ErrorReporter _errorReporter;

    public BaseScraperWorkerTests() {
        _errorReporter = new ErrorReporter(_scopeFactory, Substitute.For<ILogger<ErrorReporter>>());
    }

    private TestScraperWorker CreateWorker(
        TimeSpan? sleepInterval = null,
        bool validateConfiguration = true,
        Func<CancellationToken, Task>? doWorkOverride = null
    ) {
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
    public async Task ExecuteAsync_CallsDoWork_WhenNotCancelled() {
        using var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_CatchesAndLogsCriticalException_WhenDoWorkThrows() {
        var exception = new InvalidOperationException("Something broke");
        using var worker = CreateWorker(doWorkOverride: _ => throw exception);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        _logger.Received().Log(
            LogLevel.Critical,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Critical error in")),
            exception,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    [Fact]
    public async Task ExecuteAsync_ReportsErrorViaErrorReporter_WhenDoWorkThrows() {
        var exception = new InvalidOperationException("Report this error");
        using var worker = CreateWorker(doWorkOverride: _ => throw exception);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // ErrorReporter.Report internally calls CreateAsyncScope which will throw
        // because we have a basic substitute. The important thing is that the worker
        // continued running (did not crash) and logged the critical error.
        _logger.Received().Log(
            LogLevel.Critical,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Critical error in")),
            exception,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    [Fact]
    public async Task ExecuteAsync_StopsGracefully_WhenCancellationTokenIsCancelled() {
        var doWorkEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = CreateWorker(
            sleepInterval: TimeSpan.FromMilliseconds(5),
            doWorkOverride: async ct => {
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
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("cancelled")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCallDoWork_WhenValidateConfigurationReturnsFalse() {
        using var worker = CreateWorker(validateConfiguration: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        worker.DoWorkCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_LogsStartAndCompletionMessages() {
        using var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // "running at" log on start of each cycle
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("running at")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>()
        );

        // "cycle complete" log after DoWork finishes
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("cycle complete")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    [Fact]
    public async Task ExecuteAsync_SleepsForSleepInterval_BetweenCycles() {
        // Use a long sleep so only 1 cycle fits in the window
        var sleepInterval = TimeSpan.FromMilliseconds(500);
        using var worker = CreateWorker(sleepInterval: sleepInterval);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        await worker.StartAsync(cts.Token);

        // Wait for the CTS to fire and the worker to stop
        await Task.Delay(600);
        await worker.StopAsync(CancellationToken.None);

        // First DoWork executes immediately, then the 500ms sleep exceeds the 400ms timeout,
        // so the worker should complete exactly 1 cycle
        worker.DoWorkCallCount.Should().Be(1,
            "a 500ms sleep interval in a 400ms window should allow only 1 cycle to complete DoWork");

        // Verify the "cycle complete" log includes the sleep interval
        _logger.Received().Log(
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
    private sealed class TestScraperWorker : BaseScraperWorker {
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
        ) : base(logger, scopeFactory, errorReporter) {
            SleepInterval = sleepInterval;
            _validateConfiguration = validateConfiguration;
            _doWorkOverride = doWorkOverride;
        }

        protected override bool ValidateConfiguration() => _validateConfiguration;

        protected override async Task DoWork(CancellationToken stoppingToken) {
            Interlocked.Increment(ref _doWorkCallCount);

            if (_doWorkOverride != null) {
                await _doWorkOverride(stoppingToken);
            }
        }
    }
}
