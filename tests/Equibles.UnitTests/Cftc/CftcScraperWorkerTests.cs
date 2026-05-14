using Equibles.Cftc.HostedService;
using Equibles.Cftc.HostedService.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Cftc;

public class CftcScraperWorkerTests
{
    [Fact]
    public void Constructor_AppliesSleepIntervalHoursFromOptionsAsTimeSpanHours()
    {
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
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            options
        );

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void ErrorSource_IsCftcScraper()
    {
        // CftcScraperWorker pulls CFTC Commitment of Traders (COT) reports — a
        // different upstream and a different data product from every other scraper
        // in the system. When BaseScraperWorker's catch-all reports a failure, it
        // tags the error with this enum value as the routing key for the issue
        // tracker. A regression that copy-pasted from a sibling worker's enum
        // value (FtdScraper, DocumentScraper, FinraScraper — all visually similar
        // ErrorSource members defined in the same Errors.Data.Models namespace)
        // would silently route every COT failure into the wrong on-call queue,
        // pointing the responder at the wrong upstream's outage page. The
        // existing SleepInterval pin doesn't touch this property, so a typo
        // or reorder has no test signal. Pin the literal enum value so any
        // future routing change is a deliberate test update.
        var options = Options.Create(new CftcScraperOptions { SleepIntervalHours = 24 });
        var sut = new TestableCftcScraperWorker(
            Substitute.For<ILogger<CftcScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            options
        );

        sut.InvokeErrorSource().Should().Be(ErrorSource.CftcScraper);
    }

    [Fact]
    public void WorkerName_IsCftcScraper()
    {
        // Tenth and final WorkerName pin in the natural-extension family (CBOE,
        // Holdings, SEC filing, FTD, Document processor, FRED, Yahoo price,
        // FINRA, Congressional trade, and now CFTC). Every BaseScraperWorker
        // subclass in src/ now has its operator-visible name pinned. WorkerName
        // flows into BaseScraperWorker's structured log scope and shows up in
        // every Serilog line — on-call greps `data/worker/logs/log<date>.txt`
        // for "CFTC scraper" when Commitments of Traders ingestion stalls.
        // Like FRED and FINRA, CFTC is an acronym and capitalization matters:
        // "CFTC" all-caps to match the regulator's own usage, "scraper"
        // lowercase to match the family. A casing normalization regression
        // to "Cftc scraper" would still write logs but break case-sensitive
        // log filters and dashboard pins that target the exact literal. With
        // FRED's "FRED scraper" and FINRA's "FINRA scraper" already pinned,
        // this completes the three-acronym pin trio guarding against a future
        // "lowercase everything but the first letter" normalization sweep.
        var options = Options.Create(new CftcScraperOptions { SleepIntervalHours = 12 });
        var sut = new TestableCftcScraperWorker(
            Substitute.For<ILogger<CftcScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            options
        );

        sut.InvokeWorkerName().Should().Be("CFTC scraper");
    }

    private sealed class TestableCftcScraperWorker : CftcScraperWorker
    {
        public TestableCftcScraperWorker(
            ILogger<CftcScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<CftcScraperOptions> options
        )
            : base(logger, scopeFactory, errorReporter, options) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;

        public string InvokeWorkerName() => WorkerName;
    }
}
