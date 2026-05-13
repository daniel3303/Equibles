using Equibles.Errors.BusinessLogic;
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

    private sealed class TestableYahooPriceScraperWorker : YahooPriceScraperWorker {
        public TestableYahooPriceScraperWorker(
            ILogger<YahooPriceScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<YahooPriceScraperOptions> options)
            : base(logger, scopeFactory, errorReporter, options) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;
    }
}
