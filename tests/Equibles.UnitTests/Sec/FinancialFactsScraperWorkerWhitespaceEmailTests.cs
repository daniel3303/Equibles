using Equibles.Errors.BusinessLogic;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsScraperWorkerWhitespaceEmailTests
{
    [Fact(Skip = "GH-916 — whitespace-only Sec:ContactEmail passes the SEC-ban gate")]
    public void ValidateConfiguration_SecContactEmailWhitespaceOnly_ReturnsFalse()
    {
        // Contract: the gate exists so the scraper never hits SEC without a usable
        // contact identity (SEC 403-bans an unidentified source). A whitespace-only
        // Sec:ContactEmail yields User-Agent "Equibles Open Source/1.0 (   )" — no
        // real contact — so the gate must reject it just like an unset value.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Sec:ContactEmail"] = "   " })
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
