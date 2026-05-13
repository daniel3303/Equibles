using Equibles.Cboe.HostedService;
using Equibles.Cboe.HostedService.Configuration;
using Equibles.Errors.BusinessLogic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Cboe;

public class CboeScraperWorkerTests {
    [Fact]
    public void Constructor_AppliesSleepIntervalHoursFromOptionsAsTimeSpanHours() {
        // CboeScraperWorker reads CboeScraperOptions.SleepIntervalHours
        // (inherited from ScraperOptions, default 24h) and stores it as a
        // TimeSpan via FromHours. CBOE publishes VIX and put/call data
        // daily after-hours; the 24h default matches that cadence. A
        // refactor that swaps FromHours for FromMinutes (or drops the
        // options read in favor of a hardcoded value) would either spam
        // CBOE's CDN or silently stretch the polling window. Pin the
        // unit conversion so the regression surfaces here.
        var options = Options.Create(new CboeScraperOptions { SleepIntervalHours = 6 });
        var sut = new TestableCboeScraperWorker(
            Substitute.For<ILogger<CboeScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()),
            options);

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(6));
    }

    private sealed class TestableCboeScraperWorker : CboeScraperWorker {
        public TestableCboeScraperWorker(
            ILogger<CboeScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<CboeScraperOptions> options)
            : base(logger, scopeFactory, errorReporter, options) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;
    }
}
