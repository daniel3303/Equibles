using Equibles.Errors.BusinessLogic;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsScraperWorkerTests
{
    [Fact]
    public void ValidateConfiguration_SecContactEmailMissing_ReturnsFalse()
    {
        // Every SEC request the financial-facts scraper makes needs a contact
        // email in the User-Agent or SEC 403-bans the source IP. The contract
        // of ValidateConfiguration is the startup gate that stops the worker
        // from looping uselessly (and risking a ban) when Sec:ContactEmail is
        // unset. Pin empty-config → false so a renamed key or stray default
        // can't ship a worker that hammers SEC without identifying itself.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
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

        sut.InvokeValidateConfiguration().Should().BeFalse();
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
