using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Finra.HostedService;
using Equibles.Finra.HostedService.Configuration;
using Equibles.Integrations.Finra.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Finra;

public class FinraScraperWorkerTests {
    [Fact]
    public void ValidateConfiguration_FinraClientNotConfigured_ReturnsFalse() {
        // FinraScraperWorker drives the daily-short-volume and short-interest scrapes
        // against the FINRA API, which uses OAuth2 client credentials. When the API key
        // and secret aren't configured, every HTTP attempt fails — and the worker would
        // loop forever burning cycles on rejected token-acquisition calls. ValidateConfiguration
        // is the startup guard that lets the worker exit cleanly with a warning instead.
        //
        // Unlike the Sec/Ftd scrapers (which read `Sec:ContactEmail` directly from
        // IConfiguration), this worker resolves `IFinraClient` from the DI scope and asks
        // its `IsConfigured` flag — so the mock has to be wired through a real
        // IServiceScopeFactory chain (factory → scope → provider → service). Pinning the
        // false-when-not-configured branch protects every short-data scrape from looping on
        // a misconfigured environment.
        var finraClient = Substitute.For<IFinraClient>();
        finraClient.IsConfigured.Returns(false);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IFinraClient)).Returns(finraClient);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new TestableFinraScraperWorker(
            Substitute.For<ILogger<FinraScraperWorker>>(),
            scopeFactory,
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            Options.Create(new FinraScraperOptions()));

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_FinraClientConfigured_ReturnsTrue() {
        // Sibling to the false-case pin above. The risk this catches is asymmetric
        // and unreachable from the not-configured sibling alone: a regression that
        // hard-codes `ValidateConfiguration => false` (defensive default during a
        // refactor) passes the not-configured test and only shows up here.
        //
        // Unlike the SEC-family workers (Sec/Ftd/Holdings) which gate on a direct
        // IConfiguration read, FinraScraperWorker delegates to `IFinraClient.IsConfigured`
        // via a resolved DI scope. That indirection adds an extra failure mode: a
        // refactor that swaps the DI lookup for a hard-coded default would slip past
        // the false sibling (mocking IsConfigured = false matches the default), and
        // only this true-case pin catches the regression by exercising the live wire
        // from `_scopeFactory.CreateScope() → GetRequiredService<IFinraClient>() → IsConfigured`
        // through to a true return.
        //
        // Without this pin, the FINRA short-volume and short-interest scrapers would
        // silently stop importing — short-data dashboards on the public site rely on
        // both feeds, and an "always-false" regression here would freeze the data with
        // no exception, no Warning log, no CI signal.
        var finraClient = Substitute.For<IFinraClient>();
        finraClient.IsConfigured.Returns(true);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IFinraClient)).Returns(finraClient);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new TestableFinraScraperWorker(
            Substitute.For<ILogger<FinraScraperWorker>>(),
            scopeFactory,
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            Options.Create(new FinraScraperOptions()));

        sut.InvokeValidateConfiguration().Should().BeTrue();
    }

    [Fact]
    public void Constructor_AppliesSleepIntervalHoursFromOptionsAsTimeSpanHours() {
        // FinraScraperWorker reads FinraScraperOptions.SleepIntervalHours
        // (inherited from ScraperOptions, default 24h) and stores it as a
        // TimeSpan via FromHours. Daily short volume publishes T+1 and
        // short interest twice monthly — 24h polling catches both with
        // headroom. A refactor that swaps FromHours for FromMinutes
        // (or drops the options read in favor of a hardcoded value)
        // would either spam FINRA's OAuth-token endpoint or silently
        // stretch the polling window to 24 days. Pin the unit
        // conversion so the regression surfaces here.
        var options = Options.Create(new FinraScraperOptions { SleepIntervalHours = 12 });
        var sut = new TestableFinraScraperWorker(
            Substitute.For<ILogger<FinraScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            options);

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void ErrorSource_IsFinraScraper() {
        // FinraScraperWorker drives the daily-short-volume and short-interest scrapes
        // against the FINRA API — a different upstream, auth model (OAuth2), and quota
        // envelope from every other scraper. When BaseScraperWorker's catch-all reports
        // a failure, it tags the error with this enum value as the routing key for the
        // issue-tracker queue. The Errors.Data.Models namespace defines a row of
        // visually-similar ErrorSource members (CftcScraper, FredScraper, CboeScraper,
        // YahooScraper) alongside FinraScraper, so a copy-paste regression that picks
        // the wrong sibling would silently misroute FINRA failures into the wrong
        // on-call queue — pointing the responder at the wrong API outage page. The
        // existing ValidateConfiguration / SleepInterval pins don't touch this
        // property, so a typo or reorder has no test signal. Pin the literal enum
        // value so any future routing change must update this test deliberately.
        var options = Options.Create(new FinraScraperOptions { SleepIntervalHours = 24 });
        var sut = new TestableFinraScraperWorker(
            Substitute.For<ILogger<FinraScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            options);

        sut.InvokeErrorSource().Should().Be(ErrorSource.FinraScraper);
    }

    private sealed class TestableFinraScraperWorker : FinraScraperWorker {
        public TestableFinraScraperWorker(
            ILogger<FinraScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FinraScraperOptions> options)
            : base(logger, scopeFactory, errorReporter, options) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;
    }
}
