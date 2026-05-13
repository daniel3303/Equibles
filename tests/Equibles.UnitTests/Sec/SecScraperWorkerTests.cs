using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class SecScraperWorkerTests {
    [Fact]
    public void ValidateConfiguration_SecContactEmailMissing_ReturnsFalse() {
        // SEC EDGAR rejects any request whose User-Agent header lacks a contact email —
        // and unlike the FTD endpoint (covered by FtdScraperWorker test, PR #137), the
        // submissions API does it with a silent 403 + IP-ban risk that affects every other
        // SEC scraper sharing the same outbound IP. ValidateConfiguration is the startup
        // guard that keeps SecScraperWorker from looping uselessly — and from triggering
        // the ban — when the operator forgot to set Sec:ContactEmail in their .env.
        //
        // This `[Fact]` mirrors the FtdScraperWorker test pattern exactly: an empty
        // in-memory configuration, a TestableSecScraperWorker subclass that exposes the
        // protected ValidateConfiguration via a public invoker. Asserts the guard returns
        // false on a missing key. A regression that renamed the config key or removed the
        // null-check would surface here rather than as a silent 403 storm in production.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();
        var sut = new TestableSecScraperWorker(
            Substitute.For<ILogger<SecScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            config);

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_SecContactEmailConfigured_ReturnsTrue() {
        // Sibling to the false-case pin above. The risk this catches is asymmetric and
        // unreachable from the empty-email sibling alone: a regression that hard-codes
        // `ValidateConfiguration => false` (defensive default during a refactor, or
        // copy-paste from a perpetually-disabled worker) passes the empty-email test
        // and only shows up here. Without this pin, an "always-false" regression would
        // silently disable the SEC scraper — submissions API and filings would stop
        // importing, and the failure mode is invisible (no exception, no Warning log
        // from the worker once it cleanly exits ExecuteAsync).
        //
        // SecScraperWorker is the entry point for the entire SEC pipeline (filings,
        // ownership, insider transactions). A silent regression here breaks every
        // downstream Sec.HostedService path. The pair (empty → false, configured → true)
        // distinguishes a working `IsNullOrEmpty` check from BOTH inversion (caught by
        // the false sibling) AND constant-return regressions (caught only here).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> {
                ["Sec:ContactEmail"] = "equibles-bot@example.com"
            })
            .Build();
        var sut = new TestableSecScraperWorker(
            Substitute.For<ILogger<SecScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            config);

        sut.InvokeValidateConfiguration().Should().BeTrue();
    }

    [Fact]
    public void SleepInterval_IsFifteenSeconds() {
        // SecScraperWorker is the only scraper in the pipeline that polls the SEC
        // submissions API at sub-minute cadence (the other workers — FTD, Holdings,
        // InsiderTrading, etc. — sleep for hours per cycle). The 15-second interval
        // sits well inside SEC EDGAR's documented 10 req/second / 600 req/minute
        // limit even with concurrent cycles, but a refactor that "tightened the loop"
        // to e.g. `TimeSpan.FromSeconds(1)` (a plausible copy-paste from a unit-test
        // helper) would cause a sustained burst that trips EDGAR's IP-level ban —
        // which affects every other SEC scraper sharing the outbound IP, not just
        // this one. The base class reads SleepInterval into a `Task.Delay` after
        // each successful cycle; lowering it has no test signal and no log warning,
        // just a silent 403 storm in production until the IP is unblocked. Pin the
        // exact value so any future change has to update this test deliberately.
        var sut = new TestableSecScraperWorker(
            Substitute.For<ILogger<SecScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            new ConfigurationBuilder().Build());

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void ErrorSource_IsDocumentScraper() {
        // SecScraperWorker handles the full SEC document-pull pipeline (submissions,
        // filings, ownership XML, insider transactions). When the BaseScraperWorker's
        // catch-all reports a failure, it tags the error with this enum value as the
        // routing key for the issue-tracker queue. Note this worker uses
        // `ErrorSource.DocumentScraper` — NOT `SecScraper` — because the SEC pipeline
        // is sliced by data-product across multiple ErrorSource enum members
        // (DocumentScraper, FtdScraper, etc.). A regression that "tidied up the name"
        // by switching to `SecScraper` would silently misroute every document-pipeline
        // failure into a different queue, defeating the operational split that lets
        // on-call distinguish "PDF parse failed" from "FTD list 404'd". The existing
        // ValidateConfiguration and SleepInterval pins don't touch this property, so
        // a typo or reorder elsewhere has no test signal — pin the literal value here.
        var sut = new TestableSecScraperWorker(
            Substitute.For<ILogger<SecScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            new ConfigurationBuilder().Build());

        sut.InvokeErrorSource().Should().Be(ErrorSource.DocumentScraper);
    }

    [Fact]
    public void WorkerName_IsSecFilingScraper() {
        // Third WorkerName pin in the codebase (after CboeScraperWorker and
        // HoldingsScraperWorker). Completes the ErrorSource / SleepInterval /
        // WorkerName triple for this worker.
        //
        // WorkerName flows into BaseScraperWorker's startup/shutdown log output
        // and the heartbeat lines that operator runbooks grep for. The Sec
        // filing scraper's display string ("SEC filing scraper") is specifically
        // distinguished from the sibling "SEC FTD scraper" and "SEC document
        // processor" workers — all three live in the same Sec.HostedService
        // namespace and produce log lines in the same Serilog file. Operator
        // runbooks split the three by the exact WorkerName prefix when
        // diagnosing "which SEC subsystem is stuck?" — a rename that collapsed
        // the distinction (e.g. dropping "filing" so the worker logs as "SEC
        // scraper") would silently merge the three workers' log lines and
        // break the per-subsystem dashboards.
        //
        // The "scraper" suffix is shared across all scraper workers in the
        // codebase (CBOE scraper, Holdings scraper, FTD scraper, ...) — pin
        // the exact "SEC filing scraper" form so a rename that drops the
        // distinguishing word can't ship silently.
        var sut = new TestableSecScraperWorker(
            Substitute.For<ILogger<SecScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            new ConfigurationBuilder().Build());

        sut.InvokeWorkerName().Should().Be("SEC filing scraper");
    }

    private sealed class TestableSecScraperWorker : SecScraperWorker {
        public TestableSecScraperWorker(
            ILogger<SecScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IConfiguration configuration)
            : base(logger, scopeFactory, errorReporter, configuration) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;

        public string InvokeWorkerName() => WorkerName;
    }
}
