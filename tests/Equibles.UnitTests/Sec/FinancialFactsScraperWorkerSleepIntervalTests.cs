using Equibles.Errors.BusinessLogic;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsScraperWorkerSleepIntervalTests
{
    // Closes the SleepInterval gap in the per-worker pin family. Every
    // other scraper worker (Cboe, Cftc, Congressional, Finra, Fred, Ftd,
    // Holdings, Sec, DocumentProcessor, YahooPrice) has a SleepInterval
    // pin asserting the constructor's `TimeSpan.FromHours(options.Value
    // .SleepIntervalHours)` conversion; FinancialFactsScraperWorker
    // alone was missing it.
    //
    // FinancialFactsScraperWorker is the heaviest SEC walker — one
    // EDGAR request per tracked company per cycle. A refactor that
    // accidentally swapped `TimeSpan.FromHours` to `TimeSpan
    // .FromMinutes` (a "harmonise with the in-cycle delays" cleanup)
    // would compile cleanly, pass every existing pin (WorkerName,
    // ErrorSource, ValidateConfiguration), and silently turn a 24-hour
    // polling cycle into a 24-minute one. That's 60× more EDGAR
    // requests; SEC bans the source IP within a few minutes of
    // sustained pace, and the entire SEC-data ingestion stack stops.
    //
    // Conversely, a refactor that dropped the options read entirely
    // (`SleepInterval = TimeSpan.FromHours(24)` hardcoded) would
    // ignore the operator's tuning and silently stretch a configured
    // 4-hour polling window to the default — Company Facts dashboard
    // freshness silently degrades with no log signal.
    //
    // Pin: feed a non-default SleepIntervalHours (4) and assert the
    // resulting SleepInterval is exactly TimeSpan.FromHours(4). The
    // value choice catches both the hours/minutes swap (would yield
    // 4 minutes, not 4 hours) AND the dropped-options-read regression
    // (would yield 24 hours, the default — not 4).
    [Fact]
    public void Constructor_AppliesSleepIntervalHoursFromOptionsAsTimeSpanHours()
    {
        var options = Options.Create(new FinancialFactsScraperOptions { SleepIntervalHours = 4 });
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
            options,
            config
        );

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(4));
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

        public TimeSpan InvokeSleepInterval() => SleepInterval;
    }
}
