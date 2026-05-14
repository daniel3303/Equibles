using Equibles.Congress.HostedService;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class CongressionalTradeScraperWorkerTests
{
    [Fact]
    public void SleepInterval_IsTwelveHours()
    {
        // The 12-hour `SleepInterval` matches the cadence at which House clerks
        // and Senate's eFD system publish new PTR filings — neither updates more
        // than a couple of times per day. A regression that tightened the loop
        // (a copy-paste of `FromHours(1)` from a faster scraper, or a careless
        // `FromMinutes(12)` typo) would burst-scrape `disclosures.house.gov`
        // and `efdsearch.senate.gov` orders of magnitude more often than
        // necessary; both endpoints are public but unstable under load, and
        // both have already throttled or briefly blocked the outbound IP in
        // the past (see the upstream Polly retries inside HouseDisclosureClient
        // / SenateDisclosureClient). The failure mode is silent because retries
        // happen inside the clients — the worker just appears to be "running"
        // while every scrape attempt 429s or 503s. Pin the literal interval so
        // any future cadence change is a deliberate test update.
        var sut = new TestableCongressionalTradeScraperWorker(
            Substitute.For<ILogger<CongressionalTradeScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void ErrorSource_IsCongressScraper()
    {
        // BaseScraperWorker tags every error reported via ErrorReporter with this
        // worker's `ErrorSource` value — the tag routes operator alerts to the
        // correct oncall dashboard and team. CongressionalTradeScraperWorker reports
        // against `ErrorSource.CongressScraper`; a regression that miswired this
        // (a copy-paste from another scraper would yield `DocumentScraper`,
        // `FtdScraper`, `HoldingsScraper`, etc.) would silently route every PTR-
        // pipeline error into the wrong dashboard, where the error itself still
        // lands in the DB but under the wrong owner — the SEC team sees alerts
        // they can't action, and the Congress-trade team sees no alerts at all.
        //
        // This pin completes the ErrorSource-tagging contract across every
        // BaseScraperWorker descendant. Sibling pins:
        //   • FtdScraperWorker.ErrorSource (#273)
        //   • SecScraperWorker.ErrorSource (#274)
        //   • DocumentProcessorWorker.ErrorSource (#275)
        //   • HoldingsScraperWorker.ErrorSource (#276)
        //   • CftcScraperWorker.ErrorSource (#278)
        //   • CboeScraperWorker.ErrorSource (#279)
        //   • FredScraperWorker.ErrorSource (#281)
        //   • FinraScraperWorker.ErrorSource (#283)
        //   • YahooPriceScraperWorker.ErrorSource (#285)
        // With this pin, the entire scraper-tagging contract is locked. Any
        // future refactor that decided to "centralize" ErrorSource through a
        // single property on BaseScraperWorker (and broke ALL the workers'
        // routings in one go) would surface across the full sibling-test suite
        // rather than leaving one orphaned silent miswire.
        var sut = new TestableCongressionalTradeScraperWorker(
            Substitute.For<ILogger<CongressionalTradeScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        sut.InvokeErrorSource().Should().Be(ErrorSource.CongressScraper);
    }

    [Fact]
    public void WorkerName_IsCongressionalTradeScraper()
    {
        // Ninth WorkerName pin in the natural-extension family (CBOE, Holdings,
        // SEC filing, FTD, Document processor, FRED, Yahoo price, FINRA, and now
        // Congressional trade). WorkerName flows into BaseScraperWorker's
        // structured log scope and shows up in every Serilog line — on-call
        // greps `data/worker/logs/log<date>.txt` for "Congressional trade
        // scraper" when STOCK Act disclosure ingestion stalls. The literal
        // qualifier "trade" matters: Congress could host more endpoints in the
        // future (member rosters, committee assignments, financial disclosures
        // beyond trades) and the natural copy-paste mistake is to leave the
        // shorter "Congress scraper" name on this worker after splitting it.
        // A merged log stream would make "Congress is down" ambiguous between
        // the trade feed and any future sibling. The mismatch between
        // WorkerName ("Congressional trade scraper") and ErrorSource
        // (`CongressScraper`) is intentional — the error enum is system-wide
        // and short, the log name is operator-facing and descriptive — so the
        // worker name carries the disambiguating qualifier. Pin the exact
        // literal so any normalization to match the shorter enum value fails.
        var sut = new TestableCongressionalTradeScraperWorker(
            Substitute.For<ILogger<CongressionalTradeScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        sut.InvokeWorkerName().Should().Be("Congressional trade scraper");
    }

    private sealed class TestableCongressionalTradeScraperWorker : CongressionalTradeScraperWorker
    {
        public TestableCongressionalTradeScraperWorker(
            ILogger<CongressionalTradeScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter
        )
            : base(logger, scopeFactory, errorReporter) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;

        public string InvokeWorkerName() => WorkerName;
    }
}
