using Equibles.Errors.BusinessLogic;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsScraperWorkerStartupDelayTests
{
    // First StartupDelay pin in the cross-worker family. No existing
    // test exercises any scraper's StartupDelay value — every worker's
    // boot-time stagger is currently invisible to CI.
    //
    // Contract (production comment at
    // FinancialFactsScraperWorker.cs:26-27):
    //   "Heaviest SEC walker (one request per tracked company) —
    //   staggered last so it doesn't drain the shared EDGAR budget
    //   before the 13F real-time sweep runs."
    // The literal value is `TimeSpan.FromMinutes(8)`. The three SEC
    // scrapers are staggered:
    //   • SecScraperWorker.StartupDelay = FromMinutes(5)
    //   • FtdScraperWorker.StartupDelay = FromMinutes(6)
    //   • FinancialFactsScraperWorker.StartupDelay = FromMinutes(8)
    // FinancialFactsScraperWorker is the LAST of the three, with an
    // 8-minute boot stagger that intentionally lets the 13F real-time
    // sweep (HoldingsScraperWorker — StartupDelay 0) get first dibs
    // on the EDGAR rate-limit budget at deploy time.
    //
    // The risks this pin uniquely catches and that no existing
    // FinancialFactsScraperWorker pin sees:
    //
    //   • FromMinutes → FromHours swap. The base class
    //     (BaseScraperWorker.cs:106) checks `StartupDelay >
    //     TimeSpan.Zero` and awaits Task.Delay(StartupDelay); a
    //     hours/minutes swap turns the 8-minute stagger into 8 HOURS
    //     of silent boot delay. The Company Facts ingest would never
    //     complete a first cycle on a typical deploy day (most CI/CD
    //     pipelines cycle the container hourly during deploys);
    //     dashboards depending on Financial Facts data would freeze
    //     post-deploy with no log signal — the scraper appears
    //     "still booting" from operator-visible state.
    //
    //   • Drop to TimeSpan.Zero — a "fix the slow boot" refactor that
    //     removed the override (falling back to BaseScraperWorker's
    //     `protected virtual TimeSpan StartupDelay => TimeSpan.Zero;`
    //     default) would compile cleanly, pass every existing pin
    //     (WorkerName, ErrorSource, SleepInterval, ValidateConfiguration),
    //     and silently race FinancialFactsScraperWorker against the
    //     13F real-time sweep at boot. SEC's rate-limit budget is
    //     shared across all scrapers; with both hitting EDGAR
    //     simultaneously at deploy time, SEC issues a temporary IP
    //     ban that takes ~30 minutes to clear, blocking the
    //     time-sensitive 13F sweep that drives the institutional-
    //     holdings dashboard's freshness SLA.
    //
    //   • Value drift — `FromMinutes(8)` → `FromMinutes(5)` (matching
    //     SecScraperWorker by mistake during a copy-paste). The
    //     intended ordering (5/6/8 for Sec/Ftd/FinFacts) would
    //     collapse with FinFacts racing the SEC filing scraper at
    //     boot — same effect on EDGAR budget, less severe than the
    //     drop-to-zero case but still a regression in the
    //     intentional staggering.
    //
    // Pin: instantiate the worker and assert
    // `InvokeStartupDelay() == TimeSpan.FromMinutes(8)`. The exact
    // value catches all three regression classes:
    //   • Hours/minutes swap → 8 hours, not 8 minutes.
    //   • Drop to default → 0, not 8 minutes.
    //   • Value drift → 5/6 minutes, not 8.
    [Fact]
    public void StartupDelay_IsEightMinutes()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();
        var sut = new TestableFinancialFactsScraperWorker(
            Substitute.For<ILogger<FinancialFactsScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new FinancialFactsScraperOptions()),
            config
        );

        sut.InvokeStartupDelay().Should().Be(TimeSpan.FromMinutes(8));
    }

    private sealed class TestableFinancialFactsScraperWorker : FinancialFactsScraperWorker
    {
        public TestableFinancialFactsScraperWorker(
            ILogger<FinancialFactsScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FinancialFactsScraperOptions> options,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, options, configuration) { }

        public TimeSpan InvokeStartupDelay() => StartupDelay;
    }
}
