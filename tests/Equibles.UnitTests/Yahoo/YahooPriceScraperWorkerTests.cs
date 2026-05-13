using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Yahoo.HostedService;
using Equibles.Yahoo.HostedService.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Yahoo;

public class YahooPriceScraperWorkerTests {
    [Fact]
    public void Constructor_AppliesSleepIntervalHoursFromOptionsAsTimeSpanHours() {
        // YahooPriceScraperWorker reads YahooPriceScraperOptions.SleepIntervalHours
        // (inherited from ScraperOptions, default 24h) and stores it as a
        // TimeSpan via FromHours. Daily-close prices update once per
        // trading day, so the 24h default matches the upstream cadence.
        // A refactor that swaps FromHours for FromMinutes (or drops the
        // options read in favor of a hardcoded value) would either hammer
        // Yahoo's rate-limited endpoints or silently stretch the polling
        // window to 24 days. Pin the unit conversion so the regression
        // surfaces here.
        var options = Options.Create(new YahooPriceScraperOptions { SleepIntervalHours = 8 });
        var sut = new TestableYahooPriceScraperWorker(
            Substitute.For<ILogger<YahooPriceScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()),
            options);

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public void ErrorSource_IsYahooPriceScraper() {
        // YahooPriceScraperWorker pulls daily-close prices from Yahoo Finance —
        // a different upstream, auth model (browser-impersonation cookies/crumb),
        // and rate-limit envelope from every other scraper in the system. When
        // BaseScraperWorker's catch-all reports a failure, it tags the error
        // with this enum value as the routing key for the issue-tracker queue.
        // The Errors.Data.Models namespace defines a row of visually-similar
        // ErrorSource members (CftcScraper, FinraScraper, FredScraper,
        // CboeScraper) alongside YahooPriceScraper, and there's also a
        // YahooFundamentalsScraper sibling that could plausibly be copy-pasted
        // here by accident — both come from the same `yahoo.com` upstream. A
        // routing typo would silently misroute every price-feed failure into
        // the wrong on-call queue, pointing the responder at the wrong Yahoo
        // endpoint's outage page or worse, at an entirely different vendor.
        // The existing SleepInterval pin doesn't touch this property. Pin
        // the literal enum value so any future routing change must update
        // this test deliberately.
        var options = Options.Create(new YahooPriceScraperOptions { SleepIntervalHours = 24 });
        var sut = new TestableYahooPriceScraperWorker(
            Substitute.For<ILogger<YahooPriceScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()),
            options);

        sut.InvokeErrorSource().Should().Be(ErrorSource.YahooPriceScraper);
    }

    private sealed class TestableYahooPriceScraperWorker : YahooPriceScraperWorker {
        public TestableYahooPriceScraperWorker(
            ILogger<YahooPriceScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<YahooPriceScraperOptions> options)
            : base(logger, scopeFactory, errorReporter, options) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;
    }
}
