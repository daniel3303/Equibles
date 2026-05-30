using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the Form ADV scraper worker's identity and config guard, mirroring the per-worker pins
/// the other SEC scrapers carry. The worker downloads from sec.gov, which rejects requests whose
/// User-Agent lacks a contact email, so <see cref="FormAdvScraperWorker.ValidateConfiguration"/>
/// must keep the worker from looping uselessly when Sec:ContactEmail is unset. ErrorSource routes
/// failures to the right queue and must stay distinct from the sibling SEC scrapers'.
/// </summary>
public class FormAdvScraperWorkerTests
{
    private static TestableWorker Build(
        IDictionary<string, string> config = null,
        FormAdvScraperOptions options = null
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string>())
            .Build();
        return new TestableWorker(
            Substitute.For<ILogger<FormAdvScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(options ?? new FormAdvScraperOptions()),
            configuration
        );
    }

    [Fact]
    public void ValidateConfiguration_SecContactEmailMissing_ReturnsFalse()
    {
        Build().InvokeValidateConfiguration().Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_SecContactEmailConfigured_ReturnsTrue()
    {
        Build(new Dictionary<string, string> { ["Sec:ContactEmail"] = "bot@example.com" })
            .InvokeValidateConfiguration()
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ErrorSource_IsFormAdvScraper()
    {
        Build().InvokeErrorSource().Should().Be(ErrorSource.FormAdvScraper);
    }

    [Fact]
    public void WorkerName_IsFormAdvScraper()
    {
        Build().InvokeWorkerName().Should().Be("Form ADV scraper");
    }

    [Fact]
    public void Constructor_AppliesSleepIntervalHoursFromOptions()
    {
        Build(options: new FormAdvScraperOptions { SleepIntervalHours = 12 })
            .InvokeSleepInterval()
            .Should()
            .Be(TimeSpan.FromHours(12));
    }

    private sealed class TestableWorker : FormAdvScraperWorker
    {
        public TestableWorker(
            ILogger<FormAdvScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FormAdvScraperOptions> options,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, options, configuration) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;

        public string InvokeWorkerName() => WorkerName;
    }
}
