using System.Text;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// End-to-end tests for <see cref="DocumentScraper"/>. The class is heavily DI-coupled
/// (8 ctor deps + per-call scoped resolves), so each test wires a real
/// <c>IServiceCollection</c> with substituted clients/normalizers/converters/persistence
/// and a real <c>EquiblesDbContext</c> on the EF Core in-memory provider for the
/// <c>CommonStockRepository</c>. <see cref="IPdfTextExtractor"/> was extracted from the
/// concrete <c>PdfTextExtractor</c> specifically to make this scaffolding mockable.
/// </summary>
public class DocumentScraperTests
{
    private static readonly DateOnly FilingDateAlpha = new(2025, 1, 14);
    private static readonly DateOnly ReportDateAlpha = new(2024, 12, 31);

    [Fact]
    public async Task ScrapeDocuments_NoCompaniesInDb_CompletesWithZeroProcessedAndNoErrors()
    {
        // Covers: ctor, BuildRetryPipeline, ScrapeDocuments outer try, CompanySyncService
        // invocation, GetAllCompaniesWithNoTracking via the GetAll().AsNoTracking() branch
        // (TickersToSync left empty), the foreach skipped because the list is empty, the
        // deferred-filings block skipped because the result has no deferrals, completion
        // logging, and finally the returned ScrapingResult shape.
        //
        // No SEC client / normalizer / converter / persistence is ever invoked because
        // there are no companies to iterate. The test substitutes them anyway so a
        // future regression that inadvertently invokes them surfaces as a verification
        // assertion failure rather than a missing-dependency exception.
        var harness = new Harness();
        await using var dbContext = harness.CreateDbContext();

        var result = await harness.BuildScraper(dbContext).ScrapeDocuments();

        result.CompaniesProcessed.Should().Be(0);
        result.DocumentsFound.Should().Be(0);
        result.DocumentsAdded.Should().Be(0);
        result.DocumentsSkipped.Should().Be(0);
        result.Errors.Should().Be(0);
        result.ErrorMessages.Should().BeEmpty();
        result.DeferredFilings.Should().BeEmpty();
        await harness.CompanySync.Received(1).SyncCompaniesFromSecApi();
        await harness
            .SecEdgarClient.DidNotReceiveWithAnyArgs()
            .GetCompanyFilings(default, default, default, default);
        await harness
            .Persistence.DidNotReceiveWithAnyArgs()
            .Save(default, default, default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task ScrapeDocuments_OneCompanyOneFiling_PersistsViaDefaultMarkdownFlow()
    {
        // Covers: ProcessCompanyDocumentsWithScope, ProcessDocumentTypeForCompany (success
        // branch through one CIK), ProcessFiling (default flow — no specialized processor
        // matches DocumentType.TenK), Exists returning false, and CreateDocument's happy
        // path: SEC client → normalizer → converter → non-empty markdown → persistence.Save.
        //
        // The filing is a 10-K so DocumentTypeExtensions.FromFormName("10-K") returns
        // DocumentType.TenK, the FilingProcessors collection is empty so the default flow
        // runs, and the converter returns a non-whitespace markdown blob so the
        // PDF-fallback branch is skipped.
        var harness = new Harness();
        await using var dbContext = harness.CreateDbContext();
        var company = SeedCompany(dbContext, ticker: "ACME", cik: "0000123456");

        harness
            .SecEdgarClient.GetCompanyFilings("0000123456", DocumentTypeFilter.TenK, null)
            .Returns([
                new FilingData
                {
                    Cik = "0000123456",
                    AccessionNumber = "0000123456-25-000001",
                    FilingDate = FilingDateAlpha,
                    ReportDate = ReportDateAlpha,
                    Form = "10-K",
                    PrimaryDocument = "acme-10k.htm",
                    DocumentUrl = "https://sec.gov/acme-10k.htm",
                },
            ]);
        harness
            .SecEdgarClient.GetDocumentContent(Arg.Any<FilingData>())
            .Returns("<html><body>raw 10-K</body></html>");
        harness
            .Normalizer.Normalize(Arg.Any<string>())
            .Returns("<html><body>normalized 10-K</body></html>");
        harness
            .Converter.Convert(Arg.Any<string>())
            .Returns("# ACME Annual Report\n\nNormalized markdown content.");
        harness
            .Persistence.Exists(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>()
            )
            .Returns(false);

        var options = new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.TenK] };
        var result = await harness.BuildScraper(dbContext, options: options).ScrapeDocuments();

        result.CompaniesProcessed.Should().Be(1);
        result.DocumentsFound.Should().Be(1);
        result.DocumentsAdded.Should().Be(1);
        result.DocumentsSkipped.Should().Be(0);
        result.Errors.Should().Be(0);
        await harness
            .Persistence.Received(1)
            .Save(
                Arg.Is<CommonStock>(c => c.Id == company.Id),
                Arg.Is<byte[]>(b => Encoding.UTF8.GetString(b).Contains("ACME Annual Report")),
                $"ACME_{DocumentType.TenK.DisplayName}_{FilingDateAlpha:yyyy-MM-dd}.txt",
                DocumentType.TenK,
                FilingDateAlpha,
                ReportDateAlpha,
                "https://sec.gov/acme-10k.htm",
                "0000123456-25-000001",
                Arg.Any<CancellationToken>()
            );
        harness.PdfTextExtractor.DidNotReceiveWithAnyArgs().Extract(default);
    }

    [Fact]
    public async Task ScrapeDocuments_FilingHandledByCustomProcessor_DelegatesToProcessorAndSkipsDefaultFlow()
    {
        // Covers: ProcessFiling's specialized-processor branch. The InsiderTrading filing
        // is a Form 4, the substitute IFilingProcessor reports CanProcess=true for
        // FormFour, so its Process is invoked and the default Exists/Save flow is
        // bypassed entirely. A Process result of true increments DocumentsAdded.
        var harness = new Harness();
        await using var dbContext = harness.CreateDbContext();
        var company = SeedCompany(dbContext, ticker: "TSLA", cik: "0001318605");

        var processor = Substitute.For<IFilingProcessor>();
        processor.CanProcess(DocumentType.FormFour).Returns(true);
        processor.Process(Arg.Any<FilingData>(), Arg.Any<CommonStock>()).Returns(true);
        harness.FilingProcessors = [processor];

        harness
            .SecEdgarClient.GetCompanyFilings("0001318605", DocumentTypeFilter.FormFour, null)
            .Returns([
                new FilingData
                {
                    Cik = "0001318605",
                    AccessionNumber = "0001318605-25-000007",
                    FilingDate = FilingDateAlpha,
                    ReportDate = ReportDateAlpha,
                    Form = "4",
                    PrimaryDocument = "form4.xml",
                    DocumentUrl = "https://sec.gov/form4.xml",
                },
            ]);

        var options = new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.FormFour] };
        var result = await harness.BuildScraper(dbContext, options: options).ScrapeDocuments();

        result.DocumentsFound.Should().Be(1);
        result.DocumentsAdded.Should().Be(1);
        await processor
            .Received(1)
            .Process(
                Arg.Is<FilingData>(f => f.AccessionNumber == "0001318605-25-000007"),
                Arg.Is<CommonStock>(c => c.Id == company.Id)
            );
        await harness
            .Persistence.DidNotReceiveWithAnyArgs()
            .Exists(default, default, default, default);
        await harness
            .Persistence.DidNotReceiveWithAnyArgs()
            .Save(default, default, default, default, default, default, default, default, default);
        harness.Converter.DidNotReceiveWithAnyArgs().Convert(default);
    }

    [Fact]
    public async Task ScrapeDocuments_TwoCompaniesEachWithTwoFilings_PersistsAllFourDocumentsViaDefaultFlow()
    {
        // Bulk-save assertion. Covers the foreach loop inside ProcessDocumentTypeForCompany
        // (multiple filings per company) AND the outer foreach inside ScrapeDocuments
        // (multiple companies per run). Each filing follows the default markdown flow,
        // so persistence.Save must be invoked exactly four times — once per (company, filing)
        // pair. The DocumentsAdded counter must therefore also be 4, and the
        // CompaniesProcessed counter 2.
        var harness = new Harness();
        await using var dbContext = harness.CreateDbContext();
        var acme = SeedCompany(dbContext, ticker: "ACME", cik: "0000111111");
        var beta = SeedCompany(dbContext, ticker: "BETA", cik: "0000222222");

        harness
            .SecEdgarClient.GetCompanyFilings("0000111111", DocumentTypeFilter.TenK, null)
            .Returns([
                BuildFiling("0000111111", "ACC-A1", "10-K"),
                BuildFiling("0000111111", "ACC-A2", "10-K"),
            ]);
        harness
            .SecEdgarClient.GetCompanyFilings("0000222222", DocumentTypeFilter.TenK, null)
            .Returns([
                BuildFiling("0000222222", "ACC-B1", "10-K"),
                BuildFiling("0000222222", "ACC-B2", "10-K"),
            ]);
        harness
            .SecEdgarClient.GetDocumentContent(Arg.Any<FilingData>())
            .Returns(call => $"<html>{call.Arg<FilingData>().AccessionNumber}</html>");
        harness.Normalizer.Normalize(Arg.Any<string>()).Returns(call => call.Arg<string>());
        harness
            .Converter.Convert(Arg.Any<string>())
            .Returns(call => $"# Document\n\nContent for {call.Arg<string>()}");
        harness
            .Persistence.Exists(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>()
            )
            .Returns(false);

        var options = new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.TenK] };
        var result = await harness.BuildScraper(dbContext, options: options).ScrapeDocuments();

        result.CompaniesProcessed.Should().Be(2);
        result.DocumentsFound.Should().Be(4);
        result.DocumentsAdded.Should().Be(4);
        result.DocumentsSkipped.Should().Be(0);
        result.Errors.Should().Be(0);
        await harness
            .Persistence.Received(4)
            .Save(
                Arg.Any<CommonStock>(),
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await harness
            .Persistence.Received(2)
            .Save(
                Arg.Is<CommonStock>(c => c.Id == acme.Id),
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await harness
            .Persistence.Received(2)
            .Save(
                Arg.Is<CommonStock>(c => c.Id == beta.Id),
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ScrapeDocuments_FilingHasUnknownFormType_SkipsWithoutPersisting()
    {
        // Covers ProcessFiling's unknown-form guard (zero-hit): a filing whose
        // Form maps to no DocumentType must be counted skipped and never reach
        // CreateDocument/persistence — a regression dropping the guard would NRE
        // on the null DocumentType for every junk form SEC ever emits.
        var harness = new Harness();
        await using var dbContext = harness.CreateDbContext();
        SeedCompany(dbContext, ticker: "ACME", cik: "0000123456");

        harness
            .SecEdgarClient.GetCompanyFilings("0000123456", DocumentTypeFilter.TenK, null)
            .Returns([BuildFiling("0000123456", "0000123456-25-000009", "NOT-A-FORM")]);

        var options = new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.TenK] };
        var result = await harness.BuildScraper(dbContext, options: options).ScrapeDocuments();

        result.DocumentsFound.Should().Be(1);
        result.DocumentsSkipped.Should().Be(1);
        result.DocumentsAdded.Should().Be(0);
        result.Errors.Should().Be(0);
        await harness
            .Persistence.DidNotReceiveWithAnyArgs()
            .Save(default, default, default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task ScrapeDocuments_PersistenceExistsThrows_RecordsErrorAndContinues()
    {
        // Covers ProcessFiling's generic catch (zero-hit): an unexpected fault
        // from the persistence layer must increment Errors and be recorded, not
        // abort the whole scrape — pins per-filing fault isolation.
        var harness = new Harness();
        await using var dbContext = harness.CreateDbContext();
        SeedCompany(dbContext, ticker: "ACME", cik: "0000123456");

        harness
            .SecEdgarClient.GetCompanyFilings("0000123456", DocumentTypeFilter.TenK, null)
            .Returns([BuildFiling("0000123456", "0000123456-25-000010", "10-K")]);
        harness
            .Persistence.Exists(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>()
            )
            .Returns<bool>(_ => throw new Exception("persistence down"));

        var options = new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.TenK] };
        var result = await harness.BuildScraper(dbContext, options: options).ScrapeDocuments();

        result.DocumentsFound.Should().Be(1);
        result.Errors.Should().Be(1);
        result
            .ErrorMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("0000123456-25-000010");
        result.DocumentsAdded.Should().Be(0);
    }

    [Fact]
    public async Task ScrapeDocuments_GetCompanyFilingsThrowsHttp_RecordsPerCikErrorAndContinues()
    {
        // Covers ProcessDocumentTypeForCompany's per-CIK HttpRequestException
        // catch (zero-hit): one CIK's filing fetch failing must be logged +
        // counted, not abort the company — the loop continues to the next CIK.
        var harness = new Harness();
        await using var dbContext = harness.CreateDbContext();
        SeedCompany(dbContext, ticker: "ACME", cik: "0000123456");

        harness
            .SecEdgarClient.GetCompanyFilings("0000123456", DocumentTypeFilter.TenK, null)
            .Returns<List<FilingData>>(_ => throw new HttpRequestException("SEC EDGAR 503"));

        var options = new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.TenK] };
        var result = await harness.BuildScraper(dbContext, options: options).ScrapeDocuments();

        result.CompaniesProcessed.Should().Be(1);
        result.DocumentsFound.Should().Be(0);
        result.Errors.Should().Be(1);
        result.ErrorMessages.Should().ContainSingle().Which.Should().Contain("0000123456");
    }

    [Fact]
    public async Task ScrapeDocuments_PersistenceExistsThrowsHttp_HitsHttpCatchAndRecordsError()
    {
        // Covers ProcessFiling's HttpRequestException catch (zero-hit), distinct
        // from the generic catch: Exists throws BEFORE CreateDocument so the
        // Polly pipeline is never entered — the HTTP-specific branch records the
        // error without the deferred-retry path.
        var harness = new Harness();
        await using var dbContext = harness.CreateDbContext();
        SeedCompany(dbContext, ticker: "ACME", cik: "0000123456");

        harness
            .SecEdgarClient.GetCompanyFilings("0000123456", DocumentTypeFilter.TenK, null)
            .Returns([BuildFiling("0000123456", "0000123456-25-000011", "10-K")]);
        harness
            .Persistence.Exists(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>()
            )
            .Returns<bool>(_ => throw new HttpRequestException("persistence HTTP fault"));

        var options = new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.TenK] };
        var result = await harness.BuildScraper(dbContext, options: options).ScrapeDocuments();

        result.DocumentsFound.Should().Be(1);
        result.Errors.Should().Be(1);
        result
            .ErrorMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("0000123456-25-000011");
        result.DocumentsAdded.Should().Be(0);
    }

    [Fact]
    public async Task ScrapeDocuments_SecReportsFiscalYearEnd_PersistsItOnTheStock()
    {
        // Covers the fiscal-year-end wiring: GetCompanyMetadata returns an
        // off-calendar "MMDD", and ProcessCompanyDocumentsWithScope persists
        // it via CommonStockManager before the (empty) document-type loop.
        var harness = new Harness();
        await using var dbContext = harness.CreateDbContext();
        var company = SeedCompany(dbContext, ticker: "AAPL", cik: "0000320193");
        harness
            .SecEdgarClient.GetCompanyMetadata("0000320193")
            .Returns(new CompanyMetadata { FiscalYearEnd = "0928" });

        await harness.BuildScraper(dbContext).ScrapeDocuments();

        var persisted = await dbContext.Set<CommonStock>().SingleAsync(c => c.Id == company.Id);
        persisted.FiscalYearEndMonth.Should().Be(9);
        persisted.FiscalYearEndDay.Should().Be(28);
    }

    [Fact]
    public async Task ScrapeDocuments_SecReportsNoFiscalYearEnd_LeavesStockUnchanged()
    {
        // The substitute returns null metadata by default; the stock's
        // fiscal-year columns must stay null rather than throw or be zeroed.
        var harness = new Harness();
        await using var dbContext = harness.CreateDbContext();
        var company = SeedCompany(dbContext, ticker: "ACME", cik: "0000123456");

        await harness.BuildScraper(dbContext).ScrapeDocuments();

        var persisted = await dbContext.Set<CommonStock>().SingleAsync(c => c.Id == company.Id);
        persisted.FiscalYearEndMonth.Should().BeNull();
        persisted.FiscalYearEndDay.Should().BeNull();
    }

    // ── helpers ──

    private static FilingData BuildFiling(string cik, string accession, string form) =>
        new()
        {
            Cik = cik,
            AccessionNumber = accession,
            FilingDate = FilingDateAlpha,
            ReportDate = ReportDateAlpha,
            Form = form,
            PrimaryDocument = $"{accession}.htm",
            DocumentUrl = $"https://sec.gov/{accession}.htm",
        };

    private static CommonStock SeedCompany(EquiblesDbContext dbContext, string ticker, string cik)
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = $"{ticker} Inc",
            Cik = cik,
        };
        dbContext.Set<CommonStock>().Add(stock);
        dbContext.SaveChanges();
        return stock;
    }

    private sealed class Harness
    {
        public ICompanySyncService CompanySync { get; } = Substitute.For<ICompanySyncService>();
        public ISecEdgarClient SecEdgarClient { get; } = Substitute.For<ISecEdgarClient>();
        public ISecDocumentHtmlNormalizer Normalizer { get; } =
            Substitute.For<ISecDocumentHtmlNormalizer>();
        public ISecDocumentHtmlToMarkdownConverter Converter { get; } =
            Substitute.For<ISecDocumentHtmlToMarkdownConverter>();
        public IDocumentPersistenceService Persistence { get; } =
            Substitute.For<IDocumentPersistenceService>();
        public IPdfTextExtractor PdfTextExtractor { get; } = Substitute.For<IPdfTextExtractor>();
        public IServiceScopeFactory ScopeFactory { get; private set; }
        public List<IFilingProcessor> FilingProcessors { get; set; } = [];

        public EquiblesDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<EquiblesDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .Options;
            var modules = new IModuleConfiguration[] { new CommonStocksModuleConfiguration() };
            var dbContext = new EquiblesDbContext(options, modules);
            dbContext.Database.EnsureCreated();
            return dbContext;
        }

        public DocumentScraper BuildScraper(
            EquiblesDbContext dbContext,
            DocumentScraperOptions options = null,
            WorkerOptions workerOptions = null
        )
        {
            // Wire a real ServiceCollection so DocumentScraper's per-call CreateAsyncScope
            // resolves the same substitute references we configure on `Harness`. Repository
            // and DbContext are scoped to keep EF Core change-tracking semantics intact.
            var services = new ServiceCollection();
            // Register the EF in-memory context as a singleton INSTANCE so MS.DI doesn't
            // dispose it when each per-call scope ends — DocumentScraper creates many
            // short-lived scopes inside one ScrapeDocuments run, and a scoped factory
            // registration would invalidate the context after the first scope.
            services.AddSingleton(dbContext);
            services.AddScoped<CommonStockRepository>();
            // DocumentScraper now resolves CommonStockManager per scope to
            // persist the SEC-sourced fiscal year-end. IPublishEndpoint is an
            // unrelated CommonStockManager ctor dep (SetCusip outbox event);
            // substituted because fiscal-year detection never publishes.
            services.AddSingleton(Substitute.For<IPublishEndpoint>());
            services.AddScoped<CommonStockManager>();
            services.AddSingleton(SecEdgarClient);
            services.AddSingleton(Normalizer);
            services.AddSingleton(Converter);
            services.AddSingleton(PdfTextExtractor);
            services.AddSingleton(Persistence);

            var provider = services.BuildServiceProvider();
            ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            var errorReporter = new ErrorReporter(
                ScopeFactory,
                Substitute.For<ILogger<ErrorReporter>>()
            );

            return new DocumentScraper(
                ScopeFactory,
                CompanySync,
                FilingProcessors,
                Options.Create(options ?? new DocumentScraperOptions { DocumentTypesToSync = [] }),
                Options.Create(workerOptions ?? new WorkerOptions()),
                Substitute.For<ILogger<DocumentScraper>>(),
                errorReporter
            );
        }
    }
}
