using System.Reflection;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Media.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins that <c>DocumentScraper.ProcessCompanyDocumentsWithScope</c> reports
/// every accession the enumeration returned — including already-ingested ones
/// — to <c>IFilingDiscoveryService.MarkAccessionsEnumerated</c>. That report is
/// what releases a pending feed-flagged filing from the discovery retry
/// ledger; skipping it would keep re-enumerating the company forever.
/// </summary>
public class DocumentScraperEnumeratedAccessionReportingTests
{
    private readonly ISecEdgarClient _secEdgarClient = Substitute.For<ISecEdgarClient>();
    private readonly IDocumentPersistenceService _persistence =
        Substitute.For<IDocumentPersistenceService>();
    private readonly IFilingDiscoveryService _discovery = Substitute.For<IFilingDiscoveryService>();

    private static EquiblesFinancialDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new DocumentOnlyModuleConfiguration(),
                new MediaModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private DocumentScraper BuildScraper(
        EquiblesFinancialDbContext dbContext,
        DocumentScraperOptions options
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddScoped<CommonStockRepository>();
        services.AddScoped<DocumentRepository>();
        services.AddSingleton(Substitute.For<IBus>());
        services.AddScoped<CommonStockManager>();
        services.AddSingleton(_secEdgarClient);
        services.AddSingleton(_persistence);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new DocumentScraper(
            scopeFactory,
            Substitute.For<ICompanySyncService>(),
            _discovery,
            new List<IFilingProcessor>(),
            Options.Create(options),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<DocumentScraper>>(),
            new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>())
        );
    }

    private static CommonStock SeedCompany(EquiblesFinancialDbContext db)
    {
        var stock = new CommonStock
        {
            Ticker = "EBS",
            Name = "Emergent BioSolutions Inc.",
            Cik = "1367644",
        };
        db.Set<CommonStock>().Add(stock);
        db.SaveChanges();
        return stock;
    }

    private static Task InvokeProcess(
        DocumentScraper scraper,
        CommonStock company,
        ScrapingResult result
    )
    {
        var m = typeof(DocumentScraper).GetMethod(
            "ProcessCompanyDocumentsWithScope",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        return (Task)m.Invoke(scraper, [company, result]);
    }

    [Fact]
    public async Task ProcessCompanyDocumentsWithScope_ReportsEnumeratedAccessions_EvenWhenAlreadyIngested()
    {
        using var db = NewDbContext();
        var company = SeedCompany(db);
        const string newAccession = "0001367644-26-000080";
        const string knownAccession = "0001367644-26-000070";

        _discovery.HasPendingFeedAccessions.Returns(true);
        _secEdgarClient
            .GetCompanyFilings(
                Arg.Any<string>(),
                Arg.Any<DocumentTypeFilter?>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            )
            .Returns([
                new FilingData
                {
                    AccessionNumber = newAccession,
                    Cik = "1367644",
                    Form = "8-K",
                },
                new FilingData
                {
                    AccessionNumber = knownAccession,
                    Cik = "1367644",
                    Form = "8-K",
                },
            ]);
        // Both filings already ingested: the report must still carry them, since
        // "enumeration saw it" — not "ingest succeeded" — releases the pending
        // retry in discovery.
        _persistence
            .GetKnownFilingKeys(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                (
                    new HashSet<string> { newAccession, knownAccession },
                    new HashSet<(DateOnly, DateOnly)>()
                )
            );

        var scraper = BuildScraper(
            db,
            new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.EightK] }
        );

        await InvokeProcess(scraper, company, new ScrapingResult());

        _discovery
            .Received(1)
            .MarkAccessionsEnumerated(
                Arg.Is<IReadOnlyCollection<(string AccessionNumber, string Cik)>>(a =>
                    a.Any(p => p.AccessionNumber == newAccession && p.Cik == "1367644")
                    && a.Any(p => p.AccessionNumber == knownAccession && p.Cik == "1367644")
                )
            );
    }

    [Fact]
    public async Task ProcessCompanyDocumentsWithScope_EnumerationFailure_ReportsNothingForThatType()
    {
        using var db = NewDbContext();
        var company = SeedCompany(db);

        _discovery.HasPendingFeedAccessions.Returns(true);
        // A dead enumeration must not report accessions it never saw — the
        // pending entry has to survive so the company is retried.
        _secEdgarClient
            .GetCompanyFilings(
                Arg.Any<string>(),
                Arg.Any<DocumentTypeFilter?>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            )
            .Returns<Task<List<FilingData>>>(_ => throw new HttpRequestException("edgar down"));

        var scraper = BuildScraper(
            db,
            new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.EightK] }
        );

        await InvokeProcess(scraper, company, new ScrapingResult());

        _discovery
            .Received(1)
            .MarkAccessionsEnumerated(
                Arg.Is<IReadOnlyCollection<(string AccessionNumber, string Cik)>>(a => a.Count == 0)
            );
    }

    [Fact]
    public async Task ProcessCompanyDocumentsWithScope_NothingPending_SkipsCollectionAndReport()
    {
        using var db = NewDbContext();
        var company = SeedCompany(db);

        _discovery.HasPendingFeedAccessions.Returns(false);
        _secEdgarClient
            .GetCompanyFilings(
                Arg.Any<string>(),
                Arg.Any<DocumentTypeFilter?>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            )
            .Returns([]);

        var scraper = BuildScraper(
            db,
            new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.EightK] }
        );

        await InvokeProcess(scraper, company, new ScrapingResult());

        _discovery
            .DidNotReceive()
            .MarkAccessionsEnumerated(
                Arg.Any<IReadOnlyCollection<(string AccessionNumber, string Cik)>>()
            );
    }
}
