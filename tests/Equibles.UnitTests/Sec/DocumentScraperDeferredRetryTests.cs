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
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
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
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins <c>ScrapeDocuments</c>'s step-3 deferred-filings retry block. A filing
/// whose persistence check throws <see cref="InvalidOperationException"/> is
/// deferred in step 2; step 3 then retries it via CreateDocument, which fails
/// (content fetch throws), so the per-filing catch logs and counts it skipped.
/// Drives the real orchestration end-to-end (CommonStocks in-memory; no Sec
/// module, so the pgvector entity is never built).
/// </summary>
public class DocumentScraperDeferredRetryTests
{
    [Fact]
    public async Task ScrapeDocuments_FilingDeferredThenRetryFails_LogsAndCountsSkipped()
    {
        var dbOptions = new DbContextOptionsBuilder<EquiblesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesDbContext(
            dbOptions,
            [
                new CommonStocksModuleConfiguration(),
                new DocumentOnlyModuleConfiguration(),
                new MediaModuleConfiguration(),
            ]
        );
        ctx.Database.EnsureCreated();
        ctx.Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Id = Guid.NewGuid(),
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
        await ctx.SaveChangesAsync();

        var secEdgar = Substitute.For<ISecEdgarClient>();
        secEdgar
            .GetCompanyFilings(
                Arg.Any<string>(),
                Arg.Any<DocumentTypeFilter?>(),
                Arg.Any<DateOnly?>()
            )
            .Returns(
                new List<FilingData>
                {
                    new()
                    {
                        Cik = "0000320193",
                        AccessionNumber = "0000320193-25-000001",
                        FilingDate = new DateOnly(2025, 1, 15),
                        ReportDate = new DateOnly(2024, 12, 31),
                        Form = "10-K",
                    },
                }
            );
        // Step 2: persistence check throws → ProcessFiling defers the filing.
        var persistence = Substitute.For<IDocumentPersistenceService>();
        persistence
            .Exists(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>()
            )
            .Returns<bool>(_ => throw new InvalidOperationException("normalizer unavailable"));
        // Step 3: CreateDocument's content fetch throws → retry exhausts and the
        // deferred filing is logged + counted skipped.
        secEdgar
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("content gone"));

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddScoped<CommonStockRepository>();
        services.AddScoped<DocumentRepository>();
        // DocumentScraper resolves CommonStockManager per scope to persist the
        // SEC-sourced fiscal year-end; IPublishEndpoint is an unrelated ctor
        // dep (SetCusip outbox event) the fiscal-year path never uses.
        services.AddSingleton(Substitute.For<IPublishEndpoint>());
        services.AddScoped<CommonStockManager>();
        services.AddSingleton(secEdgar);
        services.AddSingleton(persistence);
        services.AddSingleton(Substitute.For<ISecDocumentHtmlNormalizer>());
        services.AddSingleton(Substitute.For<ISecDocumentHtmlToMarkdownConverter>());
        services.AddSingleton(Substitute.For<IPdfTextExtractor>());
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var scraper = new DocumentScraper(
            scopeFactory,
            Substitute.For<ICompanySyncService>(),
            new List<IFilingProcessor>(),
            Options.Create(
                new DocumentScraperOptions { DocumentTypesToSync = [DocumentType.TenK] }
            ),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<DocumentScraper>>(),
            new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>())
        );

        var result = await scraper.ScrapeDocuments();

        result.DocumentsSkipped.Should().BeGreaterThan(0, "the deferred filing failed its retry");
        result.DocumentsAdded.Should().Be(0);
        result.DeferredFilings.Should().BeEmpty("step 3 drains the deferred list");
    }
}
