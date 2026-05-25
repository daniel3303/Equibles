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
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial sibling to <see cref="DocumentScraperTests"/>, which only pins
/// the fiscal happy path (SEC returns a value / returns nothing). The
/// UpdateFiscalYearEnd docstring is explicit: it is best-effort — a metadata
/// failure "is logged and reported but never blocks document scraping". So a
/// throwing GetCompanyMetadata must be isolated: the company is still
/// processed and the fault must NOT count as a scraping error (it is reported
/// out-of-band). If the catch were mis-scoped the exception would reach the
/// per-company catch and increment result.Errors.
/// </summary>
public class DocumentScraperUpdateFiscalYearEndErrorIsolationTests
{
    [Fact]
    public async Task ScrapeDocuments_GetCompanyMetadataThrows_IsolatedAndDoesNotCountAsError()
    {
        var options = new DbContextOptionsBuilder<EquiblesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var dbContext = new EquiblesDbContext(
            options,
            [
                new CommonStocksModuleConfiguration(),
                new DocumentOnlyModuleConfiguration(),
                new MediaModuleConfiguration(),
            ]
        );
        dbContext.Database.EnsureCreated();
        var company = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "AAPL Inc",
            Cik = "0000320193",
        };
        dbContext.Set<CommonStock>().Add(company);
        dbContext.SaveChanges();

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetCompanyMetadata("0000320193")
            .Returns<CompanyMetadata>(_ => throw new HttpRequestException("SEC submissions down"));

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddScoped<CommonStockRepository>();
        services.AddScoped<DocumentRepository>();
        services.AddSingleton(Substitute.For<IPublishEndpoint>());
        services.AddScoped<CommonStockManager>();
        services.AddSingleton(secEdgarClient);
        services.AddSingleton(Substitute.For<IDocumentPersistenceService>());
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var errorReporter = new ErrorReporter(
            scopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var scraper = new DocumentScraper(
            scopeFactory,
            Substitute.For<ICompanySyncService>(),
            [],
            Options.Create(new DocumentScraperOptions { DocumentTypesToSync = [] }),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<DocumentScraper>>(),
            errorReporter
        );

        var result = await scraper.ScrapeDocuments();

        // Best-effort contract: company still processed, fault NOT counted as a
        // scraping error, fiscal columns untouched.
        result.CompaniesProcessed.Should().Be(1);
        result.Errors.Should().Be(0);
        var persisted = await dbContext.Set<CommonStock>().SingleAsync(c => c.Id == company.Id);
        persisted.FiscalYearEndMonth.Should().BeNull();
    }
}
