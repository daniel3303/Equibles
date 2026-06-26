#nullable enable

using System.Reflection;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Worker;

/// <summary>
/// Adversarial: <see cref="BaseScraperWorker"/> documents that the exponential error
/// backoff is capped at the smaller of <c>SleepInterval</c> and
/// <c>MaxErrorBackoffInterval</c>. For the real long-interval workers (SleepInterval 6h,
/// default MaxErrorBackoffInterval 15m), a sustained failure streak must therefore settle
/// at 15 minutes — NOT keep doubling unbounded, and NOT rise to the 6h SleepInterval.
/// The existing backoff test pins only the activity message/severity, not the interval
/// value, so this exercises the cap branch directly.
/// </summary>
public class BaseScraperWorkerErrorBackoffCapTests
{
    [Fact]
    public void ErrorBackoff_LongFailureStreak_CapsAtMaxErrorBackoffInterval()
    {
        using var worker = new CapTestWorker(sleepInterval: TimeSpan.FromHours(6));

        // Simulate a sustained outage: many consecutive faulted cycles.
        typeof(BaseScraperWorker)
            .GetField("_consecutiveFailures", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(worker, 10);

        var backoff = (TimeSpan)
            typeof(BaseScraperWorker)
                .GetMethod("ErrorBackoff", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(worker, null)!;

        // 1min doubled 9 times = 512min overshoots; cap = min(6h, 15min) = 15min.
        backoff.Should().Be(TimeSpan.FromMinutes(15));
    }

    private sealed class CapTestWorker : BaseScraperWorker
    {
        protected override string WorkerName => "CapTestWorker";
        protected override TimeSpan SleepInterval { get; }
        protected override ErrorSource ErrorSource => ErrorSource.Other;

        public CapTestWorker(TimeSpan sleepInterval)
            : base(
                Substitute.For<ILogger>(),
                Substitute.For<IServiceScopeFactory>(),
                new ErrorReporter(
                    Substitute.For<IServiceScopeFactory>(),
                    Substitute.For<ILogger<ErrorReporter>>()
                )
            )
        {
            SleepInterval = sleepInterval;
        }

        protected override Task DoWork(CancellationToken stoppingToken) => Task.CompletedTask;
    }
}
