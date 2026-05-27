using Equibles.Errors.BusinessLogic;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsScraperWorkerValidateConfigurationConfiguredTests
{
    // Sibling to FinancialFactsScraperWorkerTests's empty-email pin.
    // Mirrors the pattern already applied to FtdScraperWorker
    // (FtdScraperWorkerTests has both ValidateConfiguration arms
    // pinned). FinancialFactsScraperWorker is one of four SEC scrapers
    // gated on `Sec:ContactEmail` — SecScraperWorker, FtdScraperWorker,
    // HoldingsScraperWorker, and this. Three of the four have both
    // ValidateConfiguration arms pinned; this completes the fourth.
    //
    // The risk this pin uniquely catches and that is unreachable from
    // the existing empty-email sibling:
    //
    //   • An "always-false" regression — `ValidateConfiguration =>
    //     false` (a defensive default that crept in during a refactor,
    //     or a copy-paste from a perpetually-disabled worker) compiles
    //     cleanly, passes the empty-email pin (the contract there is
    //     false), AND silently disables the Financial Facts scraper
    //     in production. SEC Company Facts ingestion would stop, the
    //     financial-statement dashboard would freeze on the last
    //     successful sweep, and operators have no log signal because
    //     ValidateConfiguration returning false from a clean exit
    //     emits no Warning — the Warning is gated on the
    //     IsNullOrWhiteSpace check that wouldn't fire when the email
    //     IS configured.
    //
    //   • The pair (empty → false, configured → true) is the
    //     established pattern for distinguishing a working
    //     `IsNullOrWhiteSpace` check from BOTH inversion (caught by
    //     the false sibling) AND constant-return regressions (caught
    //     here).
    //
    // Pin: configure Sec:ContactEmail with a realistic email value
    // and assert ValidateConfiguration returns true.
    [Fact]
    public void ValidateConfiguration_SecContactEmailConfigured_ReturnsTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "equibles-bot@example.com" }
            )
            .Build();
        var sut = new TestableFinancialFactsScraperWorker(
            Substitute.For<ILogger<FinancialFactsScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new FinancialFactsScraperOptions()),
            config
        );

        sut.InvokeValidateConfiguration().Should().BeTrue();
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

        public bool InvokeValidateConfiguration() => ValidateConfiguration();
    }
}
