using Equibles.Cboe.HostedService;
using Equibles.Cboe.HostedService.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Cboe;

public class CboeScraperWorkerTests
{
    [Fact]
    public void Constructor_AppliesSleepIntervalHoursFromOptionsAsTimeSpanHours()
    {
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
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            options
        );

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(6));
    }

    [Fact]
    public void WorkerName_IsCboeScraper()
    {
        // WorkerName flows into BaseScraperWorker's startup/shutdown log output:
        //   _logger.LogInformation("Starting {WorkerName}", WorkerName);
        //   _logger.LogInformation("{WorkerName} stopped", WorkerName);
        // …and into every error report context line, plus the periodic Serilog
        // heartbeat that operator runbooks grep for to confirm the worker is alive.
        //
        // The risk this pins: a refactor that renames the string (e.g. "Cboe
        // scraper" lowercase, "CBOE Scraper" Pascal, "CBOE put/call scraper" more
        // specific) would silently break every operator runbook query that filters
        // production Serilog by the exact display string. The downstream consequences
        // are concrete:
        //   • Oncall dashboards that count CBOE-scraper heartbeats would empty out,
        //     triggering false "worker silent" alerts.
        //   • Log-aggregation queries used during post-mortems would miss CBOE
        //     entries, hiding the worker's behavior during incidents.
        //
        // No other scraper worker in the codebase has its WorkerName pinned — this
        // pin is the first in a planned family, establishing the pattern. The
        // ErrorSource and SleepInterval pins above protect routing and cadence;
        // WorkerName protects operator visibility. All three are independently
        // load-bearing and need independent regression coverage.
        var options = Options.Create(new CboeScraperOptions { SleepIntervalHours = 24 });
        var sut = new TestableCboeScraperWorker(
            Substitute.For<ILogger<CboeScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            options
        );

        sut.InvokeWorkerName().Should().Be("CBOE scraper");
    }

    [Fact]
    public void ErrorSource_IsCboeScraper()
    {
        // CboeScraperWorker pulls VIX history and put/call ratios from cboe.com —
        // a different upstream and data product from every other scraper. The
        // base `BaseScraperWorker` hands this enum value to `ErrorReporter.Report`
        // as the routing key for the issue-tracker queue. The Errors.Data.Models
        // namespace defines a long list of visually-similar ErrorSource members
        // (CftcScraper, FinraScraper, FredScraper, YahooScraper, etc.) all sitting
        // alongside CboeScraper, so a copy-paste regression that picks the wrong
        // sibling is the obvious failure mode — and it has no test signal from
        // the existing SleepInterval pin. Pin the literal enum value so any
        // future routing change must update this test deliberately.
        var options = Options.Create(new CboeScraperOptions { SleepIntervalHours = 24 });
        var sut = new TestableCboeScraperWorker(
            Substitute.For<ILogger<CboeScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            options
        );

        sut.InvokeErrorSource().Should().Be(ErrorSource.CboeScraper);
    }

    private sealed class TestableCboeScraperWorker : CboeScraperWorker
    {
        public TestableCboeScraperWorker(
            ILogger<CboeScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<CboeScraperOptions> options
        )
            : base(logger, scopeFactory, errorReporter, options) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;

        public string InvokeWorkerName() => WorkerName;
    }
}
