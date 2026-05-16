using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Extensions;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins <c>DocumentScraper.ProcessDocumentTypeForCompany</c>'s outer catch:
/// the inner per-CIK handler only catches <see cref="HttpRequestException"/>, so
/// any other failure from the SEC client must bubble to the document-type catch
/// — recorded as a company/type error and reported, not propagated.
/// </summary>
public class DocumentScraperProcessDocTypeCatchTests
{
    [Fact]
    public async Task ProcessDocumentTypeForCompany_NonHttpClientFailure_RecordsErrorAndReports()
    {
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetCompanyFilings(
                Arg.Any<string>(),
                Arg.Any<DocumentTypeFilter?>(),
                Arg.Any<DateOnly?>()
            )
            .Returns<Task<List<FilingData>>>(_ => throw new InvalidOperationException("boom"));

        var scraper = new DocumentScraper(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ICompanySyncService>(),
            new List<IFilingProcessor>(),
            Options.Create(new DocumentScraperOptions()),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<DocumentScraper>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var company = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var secFilter = DocumentType.TenK.ToSecEdgarFilter()!.Value;
        var result = new ScrapingResult();

        var method = typeof(DocumentScraper).GetMethod(
            "ProcessDocumentTypeForCompany",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        await (Task)
            method.Invoke(
                scraper,
                [
                    company,
                    DocumentType.TenK,
                    secFilter,
                    result,
                    secEdgarClient,
                    Substitute.For<IDocumentPersistenceService>(),
                ]
            );

        result.Errors.Should().Be(1, "the non-HTTP failure is caught at the document-type level");
        result
            .ErrorMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("AAPL")
            .And.Contain("boom");
    }
}
