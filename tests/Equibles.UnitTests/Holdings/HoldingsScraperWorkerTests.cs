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
