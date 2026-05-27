using Equibles.Errors.BusinessLogic;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FtdScraperWorkerStartupDelayTests
{
    // Third pin in the SEC-scraper StartupDelay family.
    // Siblings:
    //   • SecScraperWorker.StartupDelay = FromMinutes(5)  (pinned #2406)
    //   • FtdScraperWorker.StartupDelay = FromMinutes(6)  ← THIS pin
    //   • FinancialFactsScraperWorker.StartupDelay = FromMinutes(8) (pinned #2405)
    //
    // Contract (production comment at FtdScraperWorker.cs:21-22):
    //   "Staggered so the SEC scrapers don't drain the shared EDGAR
    //   request budget at deploy time before the time-sensitive 13F
    //   real-time sweep (delay 0) runs."
    // Literal value: `TimeSpan.FromMinutes(6)`.
    //
    // FtdScraperWorker is the MIDDLE of the three SEC scraper boot
    // staggers — 6 minutes places it after SecScraperWorker (5 min)
    // and before FinancialFactsScraperWorker (8 min). The staggered
    // ordering protects the 13F real-time sweep (HoldingsScraperWorker,
    // StartupDelay 0) from EDGAR rate-limit competition at deploy
    // time. Dropping or shifting this value collapses the protective
    // ordering.
    //
    // The risks this pin uniquely catches:
    //
    //   • FromMinutes → FromHours swap: 6-minute boot stagger becomes
    //     6 HOURS. The FTD ingest (failure-to-deliver data feeding
    //     the short-data dashboard) would silently halt for 6 hours
    //     post-deploy with no log signal — the scraper appears
    //     "still booting" from operator-visible state. Settlement-
    //     failure analytics would freeze on the last successful sweep.
    //
    //   • Drop to TimeSpan.Zero — a refactor that removed the override
    //     would compile, pass every existing FtdScraperWorker pin
    //     (WorkerName, ErrorSource, SleepInterval, ValidateConfiguration),
    //     and silently race FtdScraperWorker against the 13F sweep at
    //     boot.
    //
    //   • Value drift to 5 (matching Sec) or 8 (matching FinFacts) —
    //     copy-paste regression from an adjacent worker. Either
    //     collapses the three-tier stagger.
    //
    // Pin: assert `StartupDelay == TimeSpan.FromMinutes(6)`. Mirrors
    // the structural shape of the sibling SecScraperWorker /
    // FinancialFactsScraperWorker StartupDelay pins.
    [Fact]
    public void StartupDelay_IsSixMinutes()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();
        var sut = new TestableFtdScraperWorker(
            Substitute.For<ILogger<FtdScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new FtdScraperOptions()),
            config
        );

        sut.InvokeStartupDelay().Should().Be(TimeSpan.FromMinutes(6));
    }

    private sealed class TestableFtdScraperWorker : FtdScraperWorker
    {
        public TestableFtdScraperWorker(
            ILogger<FtdScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FtdScraperOptions> options,
            IConfiguration configuration
        )
            : base(
                logger,
                scopeFactory,
                errorReporter,
                options,
                Options.Create(new Equibles.Core.Configuration.WorkerOptions()),
                configuration
            ) { }

        public TimeSpan InvokeStartupDelay() => StartupDelay;
    }
}
