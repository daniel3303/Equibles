using Equibles.Cftc.HostedService;
using Equibles.Cftc.HostedService.Configuration;
using Equibles.Errors.BusinessLogic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Cftc;

public class CftcScraperWorkerTests {
    [Fact]
    public void Constructor_AppliesSleepIntervalHoursFromOptionsAsTimeSpanHours() {
        // CftcScraperWorker reads CftcScraperOptions.SleepIntervalHours
        // (inherited from ScraperOptions, default 24h) and stores it as a
        // TimeSpan via FromHours. The worker uses that interval to gate
        // CFTC COT report polling — COT publishes weekly, so the default
        // of 24h gives same-week pickup without hammering the API. A
        // refactor that swaps FromHours for FromMinutes (or drops the
        // options read in favor of a hardcoded value) would either spam
        // CFTC's API or silently stretch the polling window to 24 days.
        // Pin the unit conversion so the regression surfaces here.
        var options = Options.Create(new CftcScraperOptions { SleepIntervalHours = 12 });
        var sut = new TestableCftcScraperWorker(
            Substitute.For<ILogger<CftcScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()),
            options);

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(12));
    }

    private sealed class TestableCftcScraperWorker : CftcScraperWorker {
        public TestableCftcScraperWorker(
            ILogger<CftcScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<CftcScraperOptions> options)
            : base(logger, scopeFactory, errorReporter, options) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;
    }
}
