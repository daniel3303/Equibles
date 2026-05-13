using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.HostedService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class HoldingsScraperWorkerTests {
    [Fact]
    public void ValidateConfiguration_SecContactEmailMissing_ReturnsFalse() {
        // The Holdings scraper pulls 13F filings from SEC EDGAR, which requires a User-Agent
        // header with a contact email; without it the request is silently 403'd. ValidateConfiguration
        // is the startup guard that prevents the worker from looping uselessly when the operator
        // forgot the env var. Pin it so a careless rename of the config key (or a stray default)
        // doesn't ship a worker that burns cycles on rejected requests.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();
        var sut = new TestableHoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            Options.Create(new WorkerOptions()),
            config);

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_SecContactEmailConfigured_ReturnsTrue() {
        // Sibling to the false-case pin above. The risk this pin catches is asymmetric
        // and unreachable from the empty-email sibling alone: a regression that hard-codes
        // `ValidateConfiguration => false` (defensive default during refactor, or copy-paste
        // from another worker that's perpetually off) passes the empty-email test and only
        // shows up here. Without this pin, an "always-false" regression would silently
        // disable the entire Holdings scraper — 13F filings would stop importing, and the
        // failure mode is invisible because `return false` cleanly exits ExecuteAsync (no
        // exception, no log at Warning+ from the worker itself once it stops looping).
        //
        // The pair (empty → false, configured → true) distinguishes a working
        // `IsNullOrEmpty` check from BOTH inversion (`!IsNullOrEmpty` — caught by the
        // false sibling) AND constant-return regressions (caught only here). Pick a
        // realistic SEC-compliant contact email so a future refactor that adds format
        // validation (RFC 5321 syntax, domain whitelist) won't silently invalidate this
        // pin without a clear failure mode — the SEC actually inspects User-Agent strings
        // and rejects implausible contact emails.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> {
                ["Sec:ContactEmail"] = "equibles-bot@example.com"
            })
            .Build();
        var sut = new TestableHoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            Options.Create(new WorkerOptions()),
            config);

        sut.InvokeValidateConfiguration().Should().BeTrue();
    }

    private sealed class TestableHoldingsScraperWorker : HoldingsScraperWorker {
        public TestableHoldingsScraperWorker(
            ILogger<HoldingsScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<WorkerOptions> workerOptions,
            IConfiguration configuration)
            : base(logger, scopeFactory, errorReporter, workerOptions, configuration) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();
    }
}
