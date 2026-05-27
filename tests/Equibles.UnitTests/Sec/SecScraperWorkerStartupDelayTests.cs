using Equibles.Errors.BusinessLogic;
using Equibles.Sec.HostedService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class SecScraperWorkerStartupDelayTests
{
    // Sibling to FinancialFactsScraperWorkerStartupDelayTests (#2405).
    // SecScraperWorker is the FIRST staggered SEC scraper in the
    // three-worker boot stagger (5/6/8 minutes for Sec/Ftd/FinFacts).
    //
    // Contract (production comment at SecScraperWorker.cs:17-18):
    //   "Staggered so the SEC scrapers don't drain the shared EDGAR
    //   request budget at deploy time before the time-sensitive 13F
    //   real-time sweep (delay 0) runs."
    // Literal value: `TimeSpan.FromMinutes(5)`.
    //
    // The risks this pin uniquely catches:
    //
    //   • FromMinutes → FromHours swap. The base class awaits
    //     Task.Delay(StartupDelay); a swap turns the 5-minute boot
    //     stagger into 5 HOURS of silent delay. SEC document
    //     ingestion (10-K/Q/8-K filings, the dashboard's freshness
    //     SLA) would stop receiving new filings for 5 hours
    //     post-deploy with no log signal.
    //
    //   • Drop to TimeSpan.Zero — a "fix the slow boot" refactor that
    //     removed the override would compile cleanly, pass every
    //     existing SecScraperWorker pin (WorkerName, ErrorSource,
    //     SleepInterval, ValidateConfiguration), and silently race
    //     SecScraperWorker against the 13F real-time sweep at boot.
    //     Both hitting EDGAR simultaneously triggers SEC's IP ban
    //     (the 5-minute stagger is the documented mitigation).
    //
    //   • Value drift — collapsing to FromMinutes(6) (matching
    //     FtdScraperWorker) or FromMinutes(8) (matching
    //     FinancialFactsScraperWorker) breaks the intentional
    //     three-tier stagger.
    //
    // Pin: assert `StartupDelay == TimeSpan.FromMinutes(5)`.
    [Fact]
    public void StartupDelay_IsFiveMinutes()
    {
        var sut = new TestableSecScraperWorker(
            Substitute.For<ILogger<SecScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            new ConfigurationBuilder().Build()
        );

        sut.InvokeStartupDelay().Should().Be(TimeSpan.FromMinutes(5));
    }

    private sealed class TestableSecScraperWorker : SecScraperWorker
    {
        public TestableSecScraperWorker(
            ILogger<SecScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, configuration) { }

        public TimeSpan InvokeStartupDelay() => StartupDelay;
    }
}
