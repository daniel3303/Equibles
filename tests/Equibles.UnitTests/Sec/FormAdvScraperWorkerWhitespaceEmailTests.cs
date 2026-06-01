using Equibles.Errors.BusinessLogic;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FormAdvScraperWorkerWhitespaceEmailTests
{
    [Fact]
    public void ValidateConfiguration_SecContactEmailWhitespaceOnly_ReturnsFalse()
    {
        // Contract: a whitespace-only Sec:ContactEmail yields a User-Agent with no real
        // contact (SEC 403-bans an unidentified source), so the gate must reject it just
        // like an unset value — matching the FinancialFacts/Holdings workers.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Sec:ContactEmail"] = "   " })
            .Build();
        var sut = new TestableFormAdvScraperWorker(
            Substitute.For<ILogger<FormAdvScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new FormAdvScraperOptions()),
            config
        );

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    private sealed class TestableFormAdvScraperWorker : FormAdvScraperWorker
    {
        public TestableFormAdvScraperWorker(
            ILogger<FormAdvScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FormAdvScraperOptions> options,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, options, configuration) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();
    }
}
