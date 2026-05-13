using Equibles.Congress.HostedService;
using Equibles.Errors.BusinessLogic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class CongressionalTradeScraperWorkerTests {
    [Fact]
    public void SleepInterval_IsTwelveHours() {
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
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()));

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(12));
    }

    private sealed class TestableCongressionalTradeScraperWorker : CongressionalTradeScraperWorker {
        public TestableCongressionalTradeScraperWorker(
            ILogger<CongressionalTradeScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter)
            : base(logger, scopeFactory, errorReporter) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;
    }
}
