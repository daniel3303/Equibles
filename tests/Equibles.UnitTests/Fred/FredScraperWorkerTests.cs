using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Fred.HostedService;
using Equibles.Fred.HostedService.Configuration;
using Equibles.Integrations.Fred.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Fred;

public class FredScraperWorkerTests
{
    [Fact]
    public void ValidateConfiguration_FredClientNotConfigured_ReturnsFalse()
    {
        // The FRED scraper hits api.stlouisfed.org, which requires an API key on every
        // request and returns 400 without one. Unlike the SEC scrapers (which gate on
        // a raw IConfiguration key), FredScraperWorker delegates the check to the
        // injected IFredClient.IsConfigured — so this test pins the indirection that
        // a refactor could easily collapse (e.g. inlining the config read and dropping
        // the IFredClient hop). Without the guard the worker would loop forever
        // burning FRED 400 responses.
        var fredClient = Substitute.For<IFredClient>();
        fredClient.IsConfigured.Returns(false);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IFredClient)).Returns(fredClient);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new TestableFredScraperWorker(
            Substitute.For<ILogger<FredScraperWorker>>(),
            scopeFactory,
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new FredScraperOptions())
        );

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_FredClientConfigured_ReturnsTrue()
    {
        // Sibling to the false-case pin above. The risk this catches is asymmetric
        // and unreachable from the not-configured sibling alone: a regression that
        // hard-codes `ValidateConfiguration => false` (defensive default during a
        // refactor) passes the not-configured test and only shows up here.
        //
        // FredScraperWorker delegates the check to `IFredClient.IsConfigured` resolved
        // through a DI scope — same indirection pattern as FinraScraperWorker (pinned
        // in PR #231). A regression that swaps the DI lookup for a hard-coded default,
        // or that drops the wire entirely, would slip past the false sibling (mocking
        // IsConfigured = false matches the default) and only this true-case pin catches
        // the regression by exercising the live wire from `_scopeFactory.CreateScope()
        // → GetRequiredService<IFredClient>() → IsConfigured` through to a true return.
        //
        // Without this pin, FRED economic-indicator imports would silently stop —
        // every macro dashboard relying on FEDFUNDS / CPIAUCSL / UNRATE / etc. would
        // freeze with no exception or Warning log. The pair (not-configured → false,
        // configured → true) distinguishes a working IsConfigured wire from BOTH
        // inversion AND constant-return regressions.
        var fredClient = Substitute.For<IFredClient>();
        fredClient.IsConfigured.Returns(true);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IFredClient)).Returns(fredClient);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new TestableFredScraperWorker(
            Substitute.For<ILogger<FredScraperWorker>>(),
            scopeFactory,
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new FredScraperOptions())
        );

        sut.InvokeValidateConfiguration().Should().BeTrue();
    }

    [Fact]
    public void Constructor_AppliesSleepIntervalHoursFromOptionsAsTimeSpanHours()
    {
        // FredScraperWorker reads FredScraperOptions.SleepIntervalHours
        // (inherited from ScraperOptions, default 24h) and stores it as a
        // TimeSpan via FromHours. FRED economic series update on varying
        // cadences (daily fed-funds, monthly CPI/unemployment, quarterly
        // GDP), so the 24h default polls often enough to catch the slowest
        // important series. A refactor that swaps FromHours for FromMinutes
        // (or drops the options read in favor of a hardcoded value) would
        // either burn the FRED API budget or silently stretch the polling
        // window to 24 days. Pin the unit conversion so the regression
        // surfaces here.
        var options = Options.Create(new FredScraperOptions { SleepIntervalHours = 6 });
        var sut = new TestableFredScraperWorker(
            Substitute.For<ILogger<FredScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            options
        );

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(6));
    }

    [Fact]
    public void ErrorSource_IsFredScraper()
    {
        // FredScraperWorker pulls macro economic series from api.stlouisfed.org —
        // a different upstream and quota envelope from every other scraper. When
        // BaseScraperWorker's catch-all reports a failure, it tags the error with
        // this enum value as the routing key for the issue-tracker queue. The
        // Errors.Data.Models namespace defines a row of visually-similar
        // ErrorSource members (CftcScraper, FinraScraper, CboeScraper,
        // YahooScraper) alongside FredScraper, so a copy-paste regression that
        // picks the wrong sibling silently misroutes every macro-series failure
        // into the wrong on-call queue — pointing the responder at FINRA or
        // CFTC when the actual outage is at the St. Louis Fed. The existing
        // ValidateConfiguration / SleepInterval pins don't touch this property,
        // so a typo or reorder has no test signal. Pin the literal enum value
        // so any future routing change must update this test deliberately.
        var options = Options.Create(new FredScraperOptions { SleepIntervalHours = 24 });
        var sut = new TestableFredScraperWorker(
            Substitute.For<ILogger<FredScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            options
        );

        sut.InvokeErrorSource().Should().Be(ErrorSource.FredScraper);
    }

    [Fact]
    public void WorkerName_IsFredScraper()
    {
        // Sixth WorkerName pin in the natural-extension family (CBOE, Holdings, SEC
        // filing, FTD, Document processor, and now FRED). WorkerName flows into
        // BaseScraperWorker's structured log scope and shows up in every Serilog
        // line the worker emits — on-call greps `data/worker/logs/log<date>.txt`
        // for "FRED scraper" when the macro dashboards (FEDFUNDS, CPIAUCSL,
        // UNRATE) stop refreshing. The string is deliberately title-cased
        // ("FRED" all-caps because it's a Fed acronym, "scraper" lowercase to
        // match the family) — a casing regression to "Fred scraper" would still
        // log but break any case-sensitive log filter or dashboard pinning the
        // exact literal. FRED is the only scraper whose name carries an acronym
        // (vs "Holdings", "Document processor"), making it the most likely
        // casualty of a future "normalize the naming" refactor. Pin the exact
        // capitalization so any such normalization fails this test.
        var options = Options.Create(new FredScraperOptions { SleepIntervalHours = 24 });
        var sut = new TestableFredScraperWorker(
            Substitute.For<ILogger<FredScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            options
        );

        sut.InvokeWorkerName().Should().Be("FRED scraper");
    }

    private sealed class TestableFredScraperWorker : FredScraperWorker
    {
        public TestableFredScraperWorker(
            ILogger<FredScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FredScraperOptions> options
        )
            : base(logger, scopeFactory, errorReporter, options) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;

        public string InvokeWorkerName() => WorkerName;
    }
}
