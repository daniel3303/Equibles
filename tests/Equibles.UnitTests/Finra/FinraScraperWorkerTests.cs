using Equibles.Errors.BusinessLogic;
using Equibles.Finra.HostedService;
using Equibles.Finra.HostedService.Configuration;
using Equibles.Integrations.Finra.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Finra;

public class FinraScraperWorkerTests {
    [Fact]
    public void ValidateConfiguration_FinraClientNotConfigured_ReturnsFalse() {
        // FinraScraperWorker drives the daily-short-volume and short-interest scrapes
        // against the FINRA API, which uses OAuth2 client credentials. When the API key
        // and secret aren't configured, every HTTP attempt fails — and the worker would
        // loop forever burning cycles on rejected token-acquisition calls. ValidateConfiguration
        // is the startup guard that lets the worker exit cleanly with a warning instead.
        //
        // Unlike the Sec/Ftd scrapers (which read `Sec:ContactEmail` directly from
        // IConfiguration), this worker resolves `IFinraClient` from the DI scope and asks
        // its `IsConfigured` flag — so the mock has to be wired through a real
        // IServiceScopeFactory chain (factory → scope → provider → service). Pinning the
        // false-when-not-configured branch protects every short-data scrape from looping on
        // a misconfigured environment.
        var finraClient = Substitute.For<IFinraClient>();
        finraClient.IsConfigured.Returns(false);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IFinraClient)).Returns(finraClient);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new TestableFinraScraperWorker(
            Substitute.For<ILogger<FinraScraperWorker>>(),
            scopeFactory,
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            Options.Create(new FinraScraperOptions()));

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    private sealed class TestableFinraScraperWorker : FinraScraperWorker {
        public TestableFinraScraperWorker(
            ILogger<FinraScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FinraScraperOptions> options)
            : base(logger, scopeFactory, errorReporter, options) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();
    }
}
