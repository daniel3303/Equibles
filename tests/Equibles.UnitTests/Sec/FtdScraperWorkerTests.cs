using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FtdScraperWorkerTests
{
    [Fact]
    public void ValidateConfiguration_SecContactEmailMissing_ReturnsFalse()
    {
        // The FTD scraper hits SEC's failure-to-deliver feed, which rejects requests
        // whose User-Agent lacks a contact email with a silent 403. ValidateConfiguration
        // is the startup guard that prevents the worker from looping uselessly when the
        // operator forgot to set Sec:ContactEmail. Pin it so a careless rename of the
        // config key (or a stray default) doesn't ship a worker that burns cycles on
        // rejected requests.
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

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_SecContactEmailConfigured_ReturnsTrue()
    {
        // Sibling to the false-case pin above. The risk this catches is asymmetric and
        // unreachable from the empty-email sibling alone: a regression that hard-codes
        // `ValidateConfiguration => false` (defensive default during a refactor, or
        // copy-paste from a perpetually-disabled worker) passes the empty-email test
        // and only shows up here. Without this pin, an "always-false" regression would
        // silently disable the FTD scraper — failure-to-deliver imports would stop
        // and the failure mode is invisible (no exception, no Warning log from the
        // worker once it cleanly exits ExecuteAsync).
        //
        // FtdScraperWorker is one of three workers gated on Sec:ContactEmail (alongside
        // SecScraperWorker pinned in PR #227 and HoldingsScraperWorker pinned in PR #225).
        // FTD coverage feeds the short-data dashboard and is a load-bearing signal for
        // settlement-failure analytics — a silent disable here means stale FTD data
        // accumulates with no operator alert.
        //
        // The pair (empty → false, configured → true) distinguishes a working
        // `IsNullOrEmpty` check from BOTH inversion (caught by the false sibling) AND
        // constant-return regressions (caught only here).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "equibles-bot@example.com" }
            )
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

        sut.InvokeValidateConfiguration().Should().BeTrue();
    }

    [Fact]
    public void Constructor_AppliesSleepIntervalHoursFromOptionsAsTimeSpanHours()
    {
        // FtdScraperWorker reads FtdScraperOptions.SleepIntervalHours
        // (inherited from ScraperOptions, default 24h) and stores it as a
        // TimeSpan via FromHours. SEC publishes the FTD list ~bi-weekly,
        // so the 24h default samples for new releases without thrashing
        // the endpoint. A refactor that swaps FromHours for FromMinutes
        // (or drops the options read in favor of a hardcoded value)
        // would either spam SEC's CDN or silently stretch the polling
        // window. Pin the unit conversion so the regression surfaces here.
        // FtdScraperWorker is the only Sec.* worker that pulls its sleep
        // interval from options — the rest are hard-coded constants —
        // making this pin specific to FTD.
        var options = Options.Create(new FtdScraperOptions { SleepIntervalHours = 4 });
        var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var sut = new TestableFtdScraperWorker(
            Substitute.For<ILogger<FtdScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            options,
            config
        );

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(4));
    }

    [Fact]
    public void ErrorSource_IsFtdScraper()
    {
        // BaseScraperWorker passes the `ErrorSource` property to `ErrorReporter.Report`
        // every time it catches an exception in the scrape loop. The reporter uses that
        // enum value as the routing key for the GitHub-issue tracker: errors tagged
        // `FtdScraper` land in the FTD-specific issue queue, while errors tagged
        // `SecScraper` / `HoldingsScraper` / `CongressScraper` land in their respective
        // queues. A regression that swapped this property to a sibling worker's value
        // (a plausible copy-paste from SecScraperWorker which also lives in this
        // assembly) would silently misroute every FTD failure into the wrong queue —
        // operators triaging FTD-specific issues would see nothing while the SEC queue
        // would fill with confusing failure-to-deliver stack traces. The misclassification
        // is invisible to existing tests because they only exercise the static helpers
        // and ValidateConfiguration. Pin the literal value so the next reordering or
        // copy-paste must update this test deliberately.
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

        sut.InvokeErrorSource().Should().Be(ErrorSource.FtdScraper);
    }

    [Fact]
    public void WorkerName_IsFtdScraper()
    {
        // Fourth WorkerName pin (after CBOE, Holdings, SEC filing). Completes the
        // ErrorSource / SleepInterval / WorkerName triple for FtdScraperWorker.
        //
        // FtdScraperWorker is one of three workers in the Sec.HostedService
        // namespace — sibling to SecScraperWorker ("SEC filing scraper") and
        // DocumentProcessorWorker ("SEC document processor"). All three log to
        // the same Serilog file. Operator runbooks split them by exact
        // WorkerName prefix when triaging "which SEC subsystem is the source
        // of this 429 burst?" The "FTD scraper" string is deliberately distinct
        // from the "SEC ..." siblings (no "SEC" prefix) because the FTD data
        // set is a separate SEC product served from a different URL than the
        // filings APIs and produces a different shape of upstream errors.
        //
        // The risk: a refactor that "harmonized" the worker names by prefixing
        // all three with "SEC" (e.g. "SEC FTD scraper") would compile cleanly,
        // pass every existing pin, and silently break operator runbook queries
        // that filter for the exact "FTD scraper" prefix. The dashboards
        // tracking failure-to-deliver download cadence would silently stop
        // updating until someone noticed the FTD chart was missing.
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

        sut.InvokeWorkerName().Should().Be("FTD scraper");
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

        public bool InvokeValidateConfiguration() => ValidateConfiguration();

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;

        public string InvokeWorkerName() => WorkerName;
    }
}
