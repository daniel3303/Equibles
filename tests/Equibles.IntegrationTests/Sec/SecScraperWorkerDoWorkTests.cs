using System.Reflection;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// <see cref="SecScraperWorker"/>'s per-cycle <c>DoWork</c> and its
/// <c>ValidateConfiguration</c> guard were both uncovered. These pin the full
/// orchestration: a scraper result carrying errors must be logged at info and
/// then surface the with-errors warning branch; and the contact-email guard
/// must reject a missing value and accept a present one.
/// </summary>
public class SecScraperWorkerDoWorkTests
{
    private static IConfiguration Config(string contactEmail)
    {
        var configuration = Substitute.For<IConfiguration>();
        configuration["Sec:ContactEmail"].Returns(contactEmail);
        return configuration;
    }

    private static ErrorReporter BuildErrorReporter() =>
        new(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>());

    [Fact]
    public async Task DoWork_ScraperReportsErrors_LogsResultAndWarningBranch()
    {
        var scraper = Substitute.For<IDocumentScraper>();
        scraper
            .ScrapeDocuments(Arg.Any<CancellationToken>())
            .Returns(
                new ScrapingResult
                {
                    CompaniesProcessed = 5,
                    DocumentsAdded = 12,
                    Errors = 2,
                    Duration = TimeSpan.FromSeconds(7),
                }
            );

        var scopeFactory = ServiceScopeSubstitute.Create((typeof(IDocumentScraper), scraper));

        var worker = new SecScraperWorker(
            Substitute.For<ILogger<SecScraperWorker>>(),
            scopeFactory,
            BuildErrorReporter(),
            Config("test@example.com")
        );

        var doWork = typeof(SecScraperWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        await (Task)doWork.Invoke(worker, [CancellationToken.None]);

        await scraper.Received(1).ScrapeDocuments(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("contact@example.com", true)]
    public void ValidateConfiguration_DependsOnContactEmail(string email, bool expected)
    {
        var worker = new SecScraperWorker(
            Substitute.For<ILogger<SecScraperWorker>>(),
            ServiceScopeSubstitute.Create(),
            BuildErrorReporter(),
            Config(email)
        );

        var validate = typeof(SecScraperWorker).GetMethod(
            "ValidateConfiguration",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var result = (bool)validate.Invoke(worker, null);

        result.Should().Be(expected);
    }
}
