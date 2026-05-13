using Equibles.Errors.BusinessLogic;
using Equibles.Fred.HostedService;
using Equibles.Fred.HostedService.Configuration;
using Equibles.Integrations.Fred.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Fred;

public class FredScraperWorkerTests {
    [Fact]
    public void ValidateConfiguration_FredClientNotConfigured_ReturnsFalse() {
        // The FRED scraper hits api.stlouisfed.org, which requires an API key on every
        // request and returns 400 without one. Unlike the SEC scrapers (which gate on
        // a raw IConfiguration key), FredScraperWorker delegates the check to the
        // injected IFredClient.IsConfigured — so this test pins the indirection that
        // a refactor could easily collapse (e.g. inlining the config read and dropping
        // the IFredClient hop). Without the guard the worker would loop forever
        // burning FRED 400 responses.
        var fredClient = Substitute.For<IFredClient>();
        fredClient.IsConfigured.Returns(false);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IFredClient)).Returns(fredClient);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new TestableFredScraperWorker(
            Substitute.For<ILogger<FredScraperWorker>>(),
            scopeFactory,
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            Options.Create(new FredScraperOptions()));

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_FredClientConfigured_ReturnsTrue() {
        // Sibling to the false-case pin above. The risk this catches is asymmetric
        // and unreachable from the not-configured sibling alone: a regression that
        // hard-codes `ValidateConfiguration => false` (defensive default during a
        // refactor) passes the not-configured test and only shows up here.
        //
        // FredScraperWorker delegates the check to `IFredClient.IsConfigured` resolved
        // through a DI scope — same indirection pattern as FinraScraperWorker (pinned
        // in PR #231). A regression that swaps the DI lookup for a hard-coded default,
        // or that drops the wire entirely, would slip past the false sibling (mocking
        // IsConfigured = false matches the default) and only this true-case pin catches
        // the regression by exercising the live wire from `_scopeFactory.CreateScope()
        // → GetRequiredService<IFredClient>() → IsConfigured` through to a true return.
        //
        // Without this pin, FRED economic-indicator imports would silently stop —
        // every macro dashboard relying on FEDFUNDS / CPIAUCSL / UNRATE / etc. would
        // freeze with no exception or Warning log. The pair (not-configured → false,
        // configured → true) distinguishes a working IsConfigured wire from BOTH
        // inversion AND constant-return regressions.
        var fredClient = Substitute.For<IFredClient>();
        fredClient.IsConfigured.Returns(true);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IFredClient)).Returns(fredClient);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new TestableFredScraperWorker(
            Substitute.For<ILogger<FredScraperWorker>>(),
            scopeFactory,
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            Options.Create(new FredScraperOptions()));

        sut.InvokeValidateConfiguration().Should().BeTrue();
    }

    private sealed class TestableFredScraperWorker : FredScraperWorker {
        public TestableFredScraperWorker(
            ILogger<FredScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FredScraperOptions> options)
            : base(logger, scopeFactory, errorReporter, options) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();
    }
}
