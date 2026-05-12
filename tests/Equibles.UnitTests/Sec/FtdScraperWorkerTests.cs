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
