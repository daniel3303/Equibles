using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the uncovered arms of <c>DocumentScraper.ProcessFiling</c>: the
/// already-exists skip, a specialized processor returning false, and the
/// InvalidOperationException (defer) and HttpRequestException (record-error)
/// catch arms — each driven via a substituted persistence service / filing
/// processor so one bad filing never aborts the run.
/// </summary>
public class DocumentScraperProcessFilingTests
{
    private static CommonStock Company() =>
        new()
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };

    private static FilingData Filing() =>
        new()
        {
            Cik = "0000320193",
            AccessionNumber = "0000320193-25-000001",
            FilingDate = new DateOnly(2025, 1, 15),
            ReportDate = new DateOnly(2024, 12, 31),
            Form = "10-K",
        };

    private static DocumentScraper Build(
        IDocumentPersistenceService persistence,
        IFilingProcessor processor = null
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(persistence);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return new DocumentScraper(
            scopeFactory,
            Substitute.For<ICompanySyncService>(),
            processor == null ? new List<IFilingProcessor>() : [processor],
            Options.Create(new DocumentScraperOptions()),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<DocumentScraper>>(),
            new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>())
        );
    }

    private static async Task<ScrapingResult> InvokeProcessFiling(
        DocumentScraper scraper,
        IDocumentPersistenceService persistence
    )
    {
        var result = new ScrapingResult();
        var m = typeof(DocumentScraper).GetMethod(
            "ProcessFiling",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        await (Task)
            m.Invoke(scraper, [Company(), Filing(), DocumentType.TenK, result, persistence]);
        return result;
    }

    [Fact]
    public async Task ProcessFiling_DocumentAlreadyExists_SkipsWithoutCreating()
    {
        var persistence = Substitute.For<IDocumentPersistenceService>();
        persistence
            .Exists(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>()
            )
            .Returns(true);

        var result = await InvokeProcessFiling(Build(persistence), persistence);

        result.DocumentsSkipped.Should().Be(1);
        result.DocumentsAdded.Should().Be(0);
    }

    [Fact]
    public async Task ProcessFiling_SpecializedProcessorReturnsFalse_CountsAsSkipped()
    {
        var persistence = Substitute.For<IDocumentPersistenceService>();
        var processor = Substitute.For<IFilingProcessor>();
        processor.CanProcess(Arg.Any<DocumentType>()).Returns(true);
        processor.Process(Arg.Any<FilingData>(), Arg.Any<CommonStock>()).Returns(false);

        var result = await InvokeProcessFiling(Build(persistence, processor), persistence);

        result.DocumentsSkipped.Should().Be(1);
        result.DocumentsAdded.Should().Be(0);
    }

    [Fact]
    public async Task ProcessFiling_NormalizationFailure_DefersFiling()
    {
        var persistence = Substitute.For<IDocumentPersistenceService>();
        persistence
            .Exists(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>()
            )
            .Returns<bool>(_ => throw new InvalidOperationException("normalizer blew up"));

        var result = await InvokeProcessFiling(Build(persistence), persistence);

        result.DeferredFilings.Should().ContainSingle();
        result.Errors.Should().Be(0, "a deferral is not an error");
    }

    [Fact]
    public async Task ProcessFiling_HttpFailure_RecordsErrorWithoutDeferring()
    {
        var persistence = Substitute.For<IDocumentPersistenceService>();
        persistence
            .Exists(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>()
            )
            .Returns<bool>(_ => throw new HttpRequestException("SEC timeout"));

        var result = await InvokeProcessFiling(Build(persistence), persistence);

        result.Errors.Should().Be(1);
        result.DeferredFilings.Should().BeEmpty();
        result.ErrorMessages.Should().ContainSingle().Which.Should().Contain("AAPL");
    }
}
