using Equibles.Errors.BusinessLogic;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsScraperWorkerValidEmailTests
{
    // Sibling to the missing-email + whitespace-email pins: they cover the
    // false-return path but neither hits the trailing `return true;`. A
    // refactor that inverts the IsNullOrWhiteSpace condition or appends a
    // stray `return false;` after the if-block would silently stop the
    // FinancialFacts scraper from ever starting in production — undetectable
    // in production because the worker just sits idle. Pin the green arm
    // explicitly so any regression that always rejects a valid contact email
    // surfaces here.
    [Fact]
    public void ValidateConfiguration_SecContactEmailPresent_ReturnsTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "contact@example.com" }
            )
            .Build();
        var sut = new TestableWorker(
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

    private sealed class TestableWorker : FinancialFactsScraperWorker
    {
        public TestableWorker(
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
