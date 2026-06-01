using Equibles.Errors.BusinessLogic;
using Equibles.Sec.HostedService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class SecScraperWorkerWhitespaceEmailTests
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
        var sut = new TestableSecScraperWorker(
            Substitute.For<ILogger<SecScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            config
        );

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    private sealed class TestableSecScraperWorker : SecScraperWorker
    {
        public TestableSecScraperWorker(
            ILogger<SecScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, configuration) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();
    }
}
