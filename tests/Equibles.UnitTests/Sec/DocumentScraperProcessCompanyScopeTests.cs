using System.Reflection;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the two uncovered arms of <c>DocumentScraper.ProcessCompanyDocumentsWithScope</c>:
/// a document type with no SEC Edgar filter mapping (warn + continue), and the
/// per-company catch (a null DocumentTypesToSync makes the foreach throw, which
/// must be logged/reported as a company error rather than aborting the run).
/// </summary>
public class DocumentScraperProcessCompanyScopeTests
{
    private readonly ISecEdgarClient _secEdgarClient = Substitute.For<ISecEdgarClient>();
    private readonly IDocumentPersistenceService _persistence =
        Substitute.For<IDocumentPersistenceService>();

    private static EquiblesDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesDbContext(options, [new CommonStocksModuleConfiguration()]);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private DocumentScraper BuildScraper(
        EquiblesDbContext dbContext,
        DocumentScraperOptions options
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddScoped<CommonStockRepository>();
        // DocumentScraper resolves CommonStockManager per scope to persist the
        // SEC-sourced fiscal year-end; IPublishEndpoint is an unrelated ctor
        // dep (SetCusip outbox event) the fiscal-year path never uses.
        services.AddSingleton(Substitute.For<IPublishEndpoint>());
        services.AddScoped<CommonStockManager>();
        services.AddSingleton(_secEdgarClient);
        services.AddSingleton(_persistence);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new DocumentScraper(
            scopeFactory,
            Substitute.For<ICompanySyncService>(),
            new List<IFilingProcessor>(),
            Options.Create(options),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<DocumentScraper>>(),
            new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>())
        );
    }

    private static CommonStock SeedCompany(EquiblesDbContext db)
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
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
    public async Task ProcessCompanyDocumentsWithScope_DocumentTypeWithNoFilterMapping_WarnsAndSkips()
    {
        using var db = NewDbContext();
        var company = SeedCompany(db);
        // DocumentType.Other has no SEC Edgar filter mapping → the secFilter
        // == null branch logs a warning and continues, no error recorded.
        var scraper = BuildScraper(
            db,
            new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.Other] }
        );
        var result = new ScrapingResult();

        await InvokeProcess(scraper, company, result);

        result.Errors.Should().Be(0, "an unmapped document type is skipped, not an error");
    }

    [Fact]
    public async Task ProcessCompanyDocumentsWithScope_LoopThrows_RecordsCompanyErrorAndContinues()
    {
        using var db = NewDbContext();
        var company = SeedCompany(db);
        // Null DocumentTypesToSync makes the foreach throw; the per-company
        // catch must convert that into a recorded error (company is loaded, so
        // the catch's company.Ticker access is safe) rather than propagating.
        var scraper = BuildScraper(db, new DocumentScraperOptions { DocumentTypesToSync = null });
        var result = new ScrapingResult();

        await InvokeProcess(scraper, company, result);

        result.Errors.Should().Be(1, "the loop failure is caught and recorded per company");
        result.ErrorMessages.Should().ContainSingle().Which.Should().Contain("AAPL");
    }
}
