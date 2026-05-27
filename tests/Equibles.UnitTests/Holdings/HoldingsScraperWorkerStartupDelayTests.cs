using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.HostedService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class HoldingsScraperWorkerStartupDelayTests
{
    // Fourth pin in the cross-worker StartupDelay family, completing
    // every worker that overrides BaseScraperWorker's `=> TimeSpan.Zero`
    // default. The four staggered workers and their delays:
    //   • HoldingsScraperWorker:        FromMinutes(4)  ← THIS pin
    //   • SecScraperWorker:             FromMinutes(5)  (pinned #2406)
    //   • FtdScraperWorker:             FromMinutes(6)  (pinned #2407)
    //   • FinancialFactsScraperWorker:  FromMinutes(8)  (pinned #2405)
    //
    // Contract (production comment at HoldingsScraperWorker.cs:37-38):
    //   "Staggered so the SEC scrapers don't drain the shared EDGAR
    //   request budget at deploy time before the time-sensitive 13F
    //   real-time sweep (delay 0) runs."
    // Literal value: `TimeSpan.FromMinutes(4)`.
    //
    // HoldingsScraperWorker has the EARLIEST staggered start of the
    // four — 4 minutes — because the 13F holdings ingest is the
    // dashboard's freshness SLA driver (every institutional-portfolio
    // page tracks "last refreshed at" against this worker's last
    // cycle). The other three workers (Sec/Ftd/FinFacts at 5/6/8
    // minutes) intentionally stagger AFTER this so EDGAR's rate-limit
    // budget belongs to Holdings first.
    //
    // The risks this pin uniquely catches:
    //
    //   • FromMinutes → FromHours swap: 4 minutes → 4 hours. Holdings
    //     ingest would silently halt for 4 hours post-deploy. The
    //     "13F holdings refreshed at <timestamp>" badge on every
    //     institutional-portfolio page would freeze on the last
    //     successful sweep, and operators have no log signal —
    //     base-class Task.Delay completes silently.
    //
    //   • Drop to TimeSpan.Zero — a "fix the slow boot" refactor that
    //     removed the override would compile cleanly, pass every
    //     existing HoldingsScraperWorker pin (WorkerName, ErrorSource,
    //     SleepInterval, ValidateConfiguration), and remove the
    //     buffer that lets boot-time EDGAR contention settle before
    //     the heavy holdings sweep starts.
    //
    //   • Value drift to 5/6/8 minutes (matching a sibling worker)
    //     would shift Holdings AFTER one of the SEC scrapers in the
    //     stagger ordering — the SEC scrapers' EDGAR-budget protection
    //     comments specifically reference "the 13F real-time sweep
    //     (delay 0)" expecting Holdings to start FIRST among the
    //     staggered workers.
    //
    // Pin: assert `StartupDelay == TimeSpan.FromMinutes(4)`. Mirrors
    // the structural shape of the SEC scraper StartupDelay pins.
    [Fact]
    public void StartupDelay_IsFourMinutes()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();
        var sut = new TestableHoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            config
        );

        sut.InvokeStartupDelay().Should().Be(TimeSpan.FromMinutes(4));
    }

    private sealed class TestableHoldingsScraperWorker : HoldingsScraperWorker
    {
        public TestableHoldingsScraperWorker(
            ILogger<HoldingsScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<WorkerOptions> workerOptions,
            IConfiguration configuration
        )
            : base(
                logger,
                scopeFactory,
                errorReporter,
                workerOptions,
                configuration,
                new HoldingsRescanSignal()
            ) { }

        public TimeSpan InvokeStartupDelay() => StartupDelay;
    }
}
