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
