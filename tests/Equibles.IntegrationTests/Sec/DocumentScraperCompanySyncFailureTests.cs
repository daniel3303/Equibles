using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Unit-tier DocumentScraperTests cover success paths (no companies, 1 company /
/// 1 filing, custom processor, multi-company). The outer catch around the entire
/// scrape loop — the contract that turns a hard CompanySync failure into a
/// recorded Errors++ result + ErrorReporter ping instead of an uncaught throw —
/// is uncovered. Pins that contract: a regression that hoisted SyncCompaniesFromSecApi
/// out of the try block would crash the entire worker every cycle SEC EDGAR is
/// unhealthy, instead of recording the error and moving on with empty results.
/// </summary>
public class DocumentScraperCompanySyncFailureTests
{
    [Fact]
    public async Task ScrapeDocuments_CompanySyncThrows_RecordsErrorAndReportsToErrorReporter()
    {
        var companySync = Substitute.For<ICompanySyncService>();
        companySync.SyncCompaniesFromSecApi()
            .Returns<Task>(_ => throw new HttpRequestException("SEC EDGAR 503"));

        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = new DocumentScraper(
            Substitute.For<IServiceScopeFactory>(),
            companySync,
            Enumerable.Empty<IFilingProcessor>(),
            Options.Create(new DocumentScraperOptions()),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<DocumentScraper>>(),
            errorReporter
        );

        var result = await sut.ScrapeDocuments(CancellationToken.None);

        result.Errors.Should().Be(1);
        result.ErrorMessages.Should().ContainSingle()
            .Which.Should().Contain("SEC EDGAR 503");
        // ErrorReporter.Report must be invoked once with DocumentScraper source
        // and the operation name — pins the public-facing error contract.
        await errorReporter.Received(1).Report(
            ErrorSource.DocumentScraper,
            "DocumentScraper.ScrapeDocuments",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>()
        );
    }
}
