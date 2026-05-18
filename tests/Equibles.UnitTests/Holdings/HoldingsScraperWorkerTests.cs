using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.HostedService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class HoldingsScraperWorkerTests
{
    [Fact]
    public void ValidateConfiguration_SecContactEmailMissing_ReturnsFalse()
    {
        // The Holdings scraper pulls 13F filings from SEC EDGAR, which requires a User-Agent
        // header with a contact email; without it the request is silently 403'd. ValidateConfiguration
        // is the startup guard that prevents the worker from looping uselessly when the operator
        // forgot the env var. Pin it so a careless rename of the config key (or a stray default)
        // doesn't ship a worker that burns cycles on rejected requests.
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

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_SecContactEmailConfigured_ReturnsTrue()
    {
        // Sibling to the false-case pin above. The risk this pin catches is asymmetric
        // and unreachable from the empty-email sibling alone: a regression that hard-codes
        // `ValidateConfiguration => false` (defensive default during refactor, or copy-paste
        // from another worker that's perpetually off) passes the empty-email test and only
        // shows up here. Without this pin, an "always-false" regression would silently
        // disable the entire Holdings scraper — 13F filings would stop importing, and the
        // failure mode is invisible because `return false` cleanly exits ExecuteAsync (no
        // exception, no log at Warning+ from the worker itself once it stops looping).
        //
        // The pair (empty → false, configured → true) distinguishes a working
        // `IsNullOrEmpty` check from BOTH inversion (`!IsNullOrEmpty` — caught by the
        // false sibling) AND constant-return regressions (caught only here). Pick a
        // realistic SEC-compliant contact email so a future refactor that adds format
        // validation (RFC 5321 syntax, domain whitelist) won't silently invalidate this
        // pin without a clear failure mode — the SEC actually inspects User-Agent strings
        // and rejects implausible contact emails.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "equibles-bot@example.com" }
            )
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

        sut.InvokeValidateConfiguration().Should().BeTrue();
    }

    [Fact]
    public void SleepInterval_IsTwentyFourHours()
    {
        // The Holdings scraper pulls SEC EDGAR's quarterly 13F filing data sets —
        // by SEC mandate institutions report quarterly within 45 days of quarter
        // end, so new data lands at most a few times per quarter. The 24-hour
        // `SleepInterval` matches that cadence: a once-daily check catches every
        // newly published data set with minutes-to-hours latency at worst, with
        // plenty of margin for SEC's 10 req/s outbound cap shared across all
        // SEC scrapers. The risk this pins: a refactor that lowered the interval
        // (a copy-paste of `FromSeconds(15)` from a sibling worker, or a casual
        // `FromHours(1)` "let's check more often") would silently 24× the
        // outbound load against SEC EDGAR. Each Holdings pass also walks every
        // ProcessedDataSet row to compute the "to fetch" list — that's a real
        // DB query — and a tighter interval would burn DB time + SEC quota
        // for zero benefit (no new 13Fs to fetch). Pin the literal value so any
        // future cadence change is a deliberate test update.
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

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void ErrorSource_IsHoldingsScraper()
    {
        // BaseScraperWorker tags every error it reports through ErrorReporter
        // with this worker's `ErrorSource` value — the tag is what routes
        // operator alerts to the right oncall dashboard and the right team.
        // HoldingsScraperWorker reports against `ErrorSource.HoldingsScraper`;
        // a regression that mis-classified it (a copy-paste from the
        // sibling SEC workers would yield `DocumentScraper` or `FtdScraper`)
        // would silently route every 13F-pipeline error into the SEC
        // filings dashboard instead, where it would either be ignored (the
        // SEC team sees no familiar source identifier) or drown out the
        // genuine SEC-filing alerts. The failure mode is invisible because
        // the error itself still reaches the database — just under the
        // wrong owner.
        //
        // Siblings:
        //   • FtdScraperWorker.ErrorSource_IsFtdScraper (PR #273)
        //   • SecScraperWorker.ErrorSource_IsDocumentScraper (PR #274)
        // This pin extends the ErrorSource-tagging contract to the
        // Holdings worker — the only Sec-pipeline worker whose ErrorSource
        // value differs from its filename root (HoldingsScraperWorker ↔
        // ErrorSource.HoldingsScraper, but the worker lives under the SEC
        // umbrella alongside the other ContactEmail-gated workers, so a
        // careless harmonization is plausible).
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

        sut.InvokeErrorSource().Should().Be(ErrorSource.HoldingsScraper);
    }

    [Fact]
    public void WorkerName_IsHoldingsScraper()
    {
        // Second WorkerName pin in the codebase (CboeScraperWorker is the first).
        // WorkerName flows into BaseScraperWorker's startup/shutdown log output and
        // heartbeat lines that operator runbooks grep for. A rename here would break
        // production-log queries that filter by exact display string — oncall
        // heartbeat-count dashboards would empty out and trigger false "Holdings
        // worker silent" alerts, post-mortem log queries would miss Holdings
        // entries during 13F-ingest incidents.
        //
        // The Holdings scraper is particularly visibility-sensitive: 13F filings
        // arrive on a SEC-mandated quarterly cadence (45 days post quarter-end),
        // so operators rely on the heartbeat to confirm the worker is processing
        // the right data set during each quarterly window. A WorkerName regression
        // around quarter-end would silently break the visibility check and
        // potentially miss a stuck import that doesn't surface until the next cycle.
        //
        // Triple (ErrorSource → routing, SleepInterval → cadence, WorkerName →
        // operator visibility) is now complete for this worker.
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

        sut.InvokeWorkerName().Should().Be("Holdings scraper");
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
            : base(logger, scopeFactory, errorReporter, workerOptions, configuration, new HoldingsRescanSignal()) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;

        public string InvokeWorkerName() => WorkerName;
    }
}
