using System.Reflection;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

// Adversarial: BaseScraperWorker.ErrorBackoff is documented to cap the exponential growth at
// "the smaller of SleepInterval and MaxErrorBackoffInterval" — a worker must never back off
// longer than its own normal cycle sleep. The sibling cap tests only exercise the
// MaxErrorBackoffInterval bound (SleepInterval = 1h). This pins the opposite branch: with a
// SleepInterval shorter than MaxErrorBackoffInterval, a long fault streak must pin at
// SleepInterval, not climb to MaxErrorBackoffInterval.
public class BaseScraperWorkerErrorBackoffSleepCapTests
{
    [Fact]
    public void ErrorBackoff_SleepIntervalSmallerThanMax_CapsAtSleepInterval()
    {
        // Sleep (2 min) < default MaxErrorBackoffInterval (15 min); after many failures the
        // doubled base (1 min) far exceeds both, so the cap must be the smaller SleepInterval.
        var worker = new SleepCapWorker();
        typeof(BaseScraperWorker)
            .GetField("_consecutiveFailures", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(worker, 12);

        var backoff = (TimeSpan)
            typeof(BaseScraperWorker)
                .GetMethod("ErrorBackoff", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(worker, null)!;

        backoff.Should().Be(TimeSpan.FromMinutes(2));
    }

    private sealed class SleepCapWorker : BaseScraperWorker
    {
        public SleepCapWorker()
            : base(
                NullLogger.Instance,
                Substitute.For<IServiceScopeFactory>(),
                new ErrorReporter(
                    Substitute.For<IServiceScopeFactory>(),
                    NullLogger<ErrorReporter>.Instance
                )
            ) { }

        protected override string WorkerName => "SleepCap";
        protected override TimeSpan SleepInterval => TimeSpan.FromMinutes(2);
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        protected override Task DoWork(CancellationToken stoppingToken) => Task.CompletedTask;
    }
}
