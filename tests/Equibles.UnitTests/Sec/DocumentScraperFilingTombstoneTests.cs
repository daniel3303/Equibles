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

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the poison-filing tombstone lifecycle: a filing whose ingest fails
/// deterministically gets a backoff tombstone after the end-of-cycle retry,
/// the next enumeration skips it WITHOUT re-attempting (the 25k-calls/day
/// poison-cohort mechanism), a due retry goes through again, and a successful
/// ingest clears the row. Retries never stop — no filing is permanently lost.
/// </summary>
public class DocumentScraperFilingTombstoneTests
{
    private const string Accession = "0000320193-25-000001";

    private sealed class TombstoneOnlyModuleConfiguration : IModuleConfiguration
    {
        public void ConfigureEntities(ModelBuilder builder)
        {
            builder.Entity<FailedFilingIngest>();
        }
    }

    private static EquiblesFinancialDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .Options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new DocumentOnlyModuleConfiguration(),
                new MediaModuleConfiguration(),
                new TombstoneOnlyModuleConfiguration(),
            }
        );

    private static async Task<EquiblesFinancialDbContext> SeedCompany(
        EquiblesFinancialDbContext ctx
    )
    {
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
        return ctx;
    }

    private static ISecEdgarClient EdgarWithOneFiling()
    {
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
                        AccessionNumber = Accession,
                        FilingDate = new DateOnly(2025, 1, 15),
                        ReportDate = new DateOnly(2024, 12, 31),
                        Form = "10-K",
                    },
                }
            );
        return secEdgar;
    }

    private static DocumentScraper BuildScraper(
        EquiblesFinancialDbContext ctx,
        ISecEdgarClient secEdgar,
        IDocumentPersistenceService persistence,
        bool withWorkingConversion = false
    )
    {
        var normalizer = Substitute.For<ISecDocumentHtmlNormalizer>();
        var converter = Substitute.For<ISecDocumentHtmlToMarkdownConverter>();
        if (withWorkingConversion)
        {
            normalizer.Normalize(Arg.Any<string>()).Returns("<main>10-K</main>");
            converter.Convert(Arg.Any<string>()).Returns("# 10-K");
        }

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddScoped<CommonStockRepository>();
        services.AddScoped<DocumentRepository>();
        services.AddScoped<FailedFilingIngestRepository>();
        services.AddSingleton(Substitute.For<IBus>());
        services.AddScoped<CommonStockManager>();
        services.AddSingleton(secEdgar);
        services.AddSingleton(persistence);
        services.AddSingleton(normalizer);
        services.AddSingleton(converter);
        services.AddSingleton(Substitute.For<IPdfTextExtractor>());
        // Resolved per scope inside CreateDocument; default options keep XBRL
        // capture disabled and the 10-K form never resolves the 8-K stitcher.
        services.AddLogging();
        services.AddSingleton<IOptions<XbrlCaptureOptions>>(
            Options.Create(new XbrlCaptureOptions())
        );
        services.AddScoped<XbrlEnvelopeCaptureService>();
        services.AddSingleton<IOptions<AsFiledHtmlCaptureOptions>>(
            Options.Create(new AsFiledHtmlCaptureOptions())
        );
        services.AddScoped<AsFiledHtmlCaptureService>();
        var scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new DocumentScraper(
            scopeFactory,
            Substitute.For<ICompanySyncService>(),
            Substitute.For<IFilingDiscoveryService>(),
            new List<IFilingProcessor>(),
            Options.Create(
                new DocumentScraperOptions
                {
                    UseEventDrivenDiscovery = false,
                    DocumentTypesToSync = [DocumentType.TenK],
                }
            ),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<DocumentScraper>>(),
            new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>())
        );
    }

    private static IDocumentPersistenceService AlwaysFailingPersistence()
    {
        var persistence = Substitute.For<IDocumentPersistenceService>();
        persistence
            .Exists(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<string>()
            )
            .Returns<bool>(_ => throw new InvalidOperationException("normalizer unavailable"));
        return persistence;
    }

    [Fact]
    public async Task DeterministicFailure_CreatesTombstone_AndNextCycleSkipsWithoutRetry()
    {
        await using var ctx = await SeedCompany(CreateContext());
        var persistence = AlwaysFailingPersistence();
        var scraper = BuildScraper(ctx, EdgarWithOneFiling(), persistence);

        await scraper.ScrapeDocuments();

        var tombstone = await ctx.Set<FailedFilingIngest>().SingleAsync();
        tombstone.AccessionNumber.Should().Be(Accession);
        tombstone.AttemptCount.Should().Be(1);
        tombstone.NextRetryAt.Should().BeAfter(DateTime.UtcNow);
        tombstone.LastError.Should().Contain("normalizer unavailable");

        static int ExistsCalls(IDocumentPersistenceService p) =>
            p.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "Exists");
        var existsCallsAfterFirstCycle = ExistsCalls(persistence);

        var secondCycle = await scraper.ScrapeDocuments();

        // The tombstone prefilter dropped the filing before any ingest attempt:
        // no new Exists probe (and hence no re-download), no deferral, attempt
        // count unchanged. The batched GetKnownFilingKeys dedup lookup still
        // runs — it precedes the tombstone filter and is DB-only.
        ExistsCalls(persistence).Should().Be(existsCallsAfterFirstCycle);
        secondCycle.DocumentsSkipped.Should().Be(1);
        secondCycle.DeferredFilings.Should().BeEmpty();
        (await ctx.Set<FailedFilingIngest>().SingleAsync()).AttemptCount.Should().Be(1);
    }

    public static TheoryData<Exception> TransientFailures =>
        new()
        {
            new HttpRequestException("edgar down"),
            // HttpClient timeouts surface as TaskCanceledException.
            new TaskCanceledException("timed out"),
        };

    [Theory]
    [MemberData(nameof(TransientFailures))]
    public async Task TransientRetryFailure_IsNeverTombstoned(Exception transient)
    {
        await using var ctx = await SeedCompany(CreateContext());
        // Deterministic failure defers the filing, then the end-of-cycle retry
        // hits a transient fault — the filing must retry next enumeration
        // instead of being put on a multi-day backoff by an infra blip.
        var persistence = Substitute.For<IDocumentPersistenceService>();
        persistence
            .Exists(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<string>()
            )
            .Returns<bool>(
                _ => throw new InvalidOperationException("normalizer unavailable"),
                _ => throw transient
            );
        var scraper = BuildScraper(ctx, EdgarWithOneFiling(), persistence);

        var result = await scraper.ScrapeDocuments();

        result.DocumentsSkipped.Should().Be(1);
        (await ctx.Set<FailedFilingIngest>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DueRetryThatSucceeds_ClearsTombstone()
    {
        await using var ctx = await SeedCompany(CreateContext());
        // Pre-existing tombstone whose backoff has elapsed → the filing is due.
        ctx.Set<FailedFilingIngest>()
            .Add(
                new FailedFilingIngest
                {
                    AccessionNumber = Accession,
                    Cik = "0000320193",
                    FormType = "10-K",
                    FilingDate = new DateOnly(2025, 1, 15),
                    AttemptCount = 3,
                    LastAttemptAt = DateTime.UtcNow.AddDays(-5),
                    NextRetryAt = DateTime.UtcNow.AddDays(-1),
                    LastError = "normalizer unavailable",
                }
            );
        await ctx.SaveChangesAsync();

        // This time ingest succeeds: not yet persisted, content and conversion work.
        var persistence = Substitute.For<IDocumentPersistenceService>();
        persistence
            .Exists(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<string>()
            )
            .Returns(false);
        persistence
            .GetKnownFilingKeys(
                Arg.Any<CommonStock>(),
                Arg.Any<DocumentType>(),
                Arg.Any<IReadOnlyCollection<string>>()
            )
            .Returns(([], new HashSet<(DateOnly FilingDate, DateOnly ReportDate)>()));
        var secEdgar = EdgarWithOneFiling();
        secEdgar.GetDocumentContent(Arg.Any<FilingData>()).Returns("<html>10-K</html>");

        var scraper = BuildScraper(ctx, secEdgar, persistence, withWorkingConversion: true);

        var result = await scraper.ScrapeDocuments();

        result.DocumentsAdded.Should().Be(1);
        (await ctx.Set<FailedFilingIngest>().CountAsync()).Should().Be(0);
    }
}
