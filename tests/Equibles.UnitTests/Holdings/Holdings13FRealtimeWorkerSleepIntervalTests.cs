using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.HostedService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class Holdings13FRealtimeWorkerSleepIntervalTests
{
    // Completes the SleepInterval / ErrorSource / WorkerName triple for the
    // 13F real-time worker — WorkerName is already pinned by
    // Holdings13FRealtimeWorkerWorkerNameTests; this sibling pins the
    // 6-hour cadence.
    //
    // The 6-hour `SleepInterval` straddles the SEC's intraday filing flow:
    // 13F-HRs trickle in throughout business hours and across overnight
    // batches, so a 4x-per-day sweep surfaces fresh filings within hours
    // while the quarterly bulk import remains the authoritative reconcile.
    // The cadence is uniquely sensitive in this worker because:
    //   • Sibling quarterly HoldingsScraperWorker sleeps 24h — a copy-paste
    //     of that interval here would silently delay real-time visibility
    //     by 18+ hours, defeating the whole "second to a real-time" promise.
    //   • A tighter interval (e.g. FromMinutes(6) from a typo) would burst
    //     against EDGAR's daily-index endpoint and chew through the SEC
    //     request budget the worker shares with the rest of the SEC
    //     pipeline.
    //
    // Pin the literal value so any future change has to update this
    // test deliberately. Distinguishes a working `FromHours(6)` from
    // both `FromHours(24)` (quarterly-worker copy-paste) and
    // `FromMinutes(6)` (typo).
    [Fact]
    public void SleepInterval_IsSixHours()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();
        var sut = new TestableHoldings13FRealtimeWorker(
            Substitute.For<ILogger<Holdings13FRealtimeWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            config
        );

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(6));
    }

    private sealed class TestableHoldings13FRealtimeWorker : Holdings13FRealtimeWorker
    {
        public TestableHoldings13FRealtimeWorker(
            ILogger<Holdings13FRealtimeWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<WorkerOptions> workerOptions,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, workerOptions, configuration) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;
    }
}
