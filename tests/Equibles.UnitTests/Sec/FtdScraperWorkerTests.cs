using Equibles.Errors.BusinessLogic;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FtdScraperWorkerTests {
    [Fact]
    public void ValidateConfiguration_SecContactEmailMissing_ReturnsFalse() {
        // The FTD scraper hits SEC's failure-to-deliver feed, which rejects requests
        // whose User-Agent lacks a contact email with a silent 403. ValidateConfiguration
        // is the startup guard that prevents the worker from looping uselessly when the
        // operator forgot to set Sec:ContactEmail. Pin it so a careless rename of the
        // config key (or a stray default) doesn't ship a worker that burns cycles on
        // rejected requests.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();
        var sut = new TestableFtdScraperWorker(
            Substitute.For<ILogger<FtdScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            Options.Create(new FtdScraperOptions()),
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
        // silently disable the FTD scraper — failure-to-deliver imports would stop
        // and the failure mode is invisible (no exception, no Warning log from the
        // worker once it cleanly exits ExecuteAsync).
        //
        // FtdScraperWorker is one of three workers gated on Sec:ContactEmail (alongside
        // SecScraperWorker pinned in PR #227 and HoldingsScraperWorker pinned in PR #225).
        // FTD coverage feeds the short-data dashboard and is a load-bearing signal for
        // settlement-failure analytics — a silent disable here means stale FTD data
        // accumulates with no operator alert.
        //
        // The pair (empty → false, configured → true) distinguishes a working
        // `IsNullOrEmpty` check from BOTH inversion (caught by the false sibling) AND
        // constant-return regressions (caught only here).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> {
                ["Sec:ContactEmail"] = "equibles-bot@example.com"
            })
            .Build();
        var sut = new TestableFtdScraperWorker(
            Substitute.For<ILogger<FtdScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            Options.Create(new FtdScraperOptions()),
            config);

        sut.InvokeValidateConfiguration().Should().BeTrue();
    }

    private sealed class TestableFtdScraperWorker : FtdScraperWorker {
        public TestableFtdScraperWorker(
            ILogger<FtdScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FtdScraperOptions> options,
            IConfiguration configuration)
            : base(logger, scopeFactory, errorReporter, options, configuration) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();
    }
}
