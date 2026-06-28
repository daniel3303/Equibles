using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract: <c>FilingsWebsiteSource</c> reads the website disclosure from the
/// stock's most recent stored filing, trying 10-K first (where the disclosure is
/// mandated), then DEF 14A, then 10-Q — and reads the most recent filing of a
/// type, since a company's website can change over its filing history. The filing
/// body is stored as ordered <c>Chunk</c> rows (not on <c>Document.Content</c>), so
/// the source reassembles the text from those chunks.
/// </summary>
public class FilingsWebsiteSourceTests
{
    private static DbContextOptions<EquiblesFinancialDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .Options;

    private static EquiblesFinancialDbContext NewContext(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new DocumentAndChunkModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static FilingsWebsiteSource BuildSut(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(options));
        services.AddScoped<DocumentRepository>();
        services.AddScoped<ChunkRepository>();
        return new FilingsWebsiteSource(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<FilingsWebsiteSource>>()
        );
    }

    /// <summary>
    /// Seeds a filing whose body is split across <paramref name="chunkContents"/> in
    /// order — mirroring how the ingestion pipeline stores the text as chunks.
    /// </summary>
    private static async Task SeedFiling(
        DbContextOptions<EquiblesFinancialDbContext> options,
        Guid stockId,
        DocumentType type,
        DateOnly reportingDate,
        params string[] chunkContents
    )
    {
        using var ctx = NewContext(options);
        var document = new Document
        {
            CommonStockId = stockId,
            DocumentType = type,
            ReportingDate = reportingDate,
        };
        ctx.Set<Document>().Add(document);
        await ctx.SaveChangesAsync();

        for (var i = 0; i < chunkContents.Length; i++)
            ctx.Set<Chunk>()
                .Add(
                    new Chunk
                    {
                        DocumentId = document.Id,
                        Index = i,
                        Content = chunkContents[i],
                        DocumentType = type,
                        Ticker = "EXM",
                        ReportingDate = reportingDate.ToDateTime(TimeOnly.MinValue),
                    }
                );
        await ctx.SaveChangesAsync();
    }

    private static WebsiteSourceStock Stock(Guid id) => new(id, "EXM", "0000000002");

    [Fact]
    public async Task TenKDisclosure_Wins_OverProxyAndTenQ()
    {
        var options = NewDbOptions();
        var stockId = Guid.NewGuid();
        await SeedFiling(
            options,
            stockId,
            DocumentType.TenK,
            new DateOnly(2025, 9, 27),
            "Our website address is www.from-10k.com."
        );
        await SeedFiling(
            options,
            stockId,
            DocumentType.Def14A,
            new DateOnly(2026, 1, 10),
            "Materials are posted on our website at www.from-proxy.com."
        );

        var results = await BuildSut(options)
            .FindWebsites([Stock(stockId)], CancellationToken.None);

        results.Should().ContainSingle().Which.Value.Should().Be("www.from-10k.com");
    }

    [Fact]
    public async Task MostRecentTenK_IsTheOneRead()
    {
        var options = NewDbOptions();
        var stockId = Guid.NewGuid();
        await SeedFiling(
            options,
            stockId,
            DocumentType.TenK,
            new DateOnly(2024, 9, 28),
            "Our website address is www.old-domain.com."
        );
        await SeedFiling(
            options,
            stockId,
            DocumentType.TenK,
            new DateOnly(2025, 9, 27),
            "Our website address is www.new-domain.com."
        );

        var results = await BuildSut(options)
            .FindWebsites([Stock(stockId)], CancellationToken.None);

        results[stockId].Should().Be("www.new-domain.com");
    }

    [Fact]
    public async Task NoTenK_FallsBackToProxy_ThenTenQ()
    {
        var options = NewDbOptions();
        var proxyOnly = Guid.NewGuid();
        var tenQOnly = Guid.NewGuid();
        await SeedFiling(
            options,
            proxyOnly,
            DocumentType.Def14A,
            new DateOnly(2026, 1, 10),
            "Materials are posted on our website at www.from-proxy.com."
        );
        await SeedFiling(
            options,
            tenQOnly,
            DocumentType.TenQ,
            new DateOnly(2026, 3, 31),
            "We provide information for investors on our corporate website, www.from-10q.com."
        );

        var results = await BuildSut(options)
            .FindWebsites([Stock(proxyOnly), Stock(tenQOnly)], CancellationToken.None);

        results[proxyOnly].Should().Be("www.from-proxy.com");
        results[tenQOnly].Should().Be("www.from-10q.com");
    }

    [Fact]
    public async Task DisclosureInALaterChunk_IsReassembledAndFound()
    {
        var options = NewDbOptions();
        var stockId = Guid.NewGuid();
        // The disclosure lives past the first chunk: the source must read every chunk
        // in Index order, not just chunk 0.
        await SeedFiling(
            options,
            stockId,
            DocumentType.TenK,
            new DateOnly(2025, 9, 27),
            "Item 1. Business. The company designs and sells products worldwide.",
            "Available Information. Our website address is www.later-chunk.com, where filings are posted."
        );

        var results = await BuildSut(options)
            .FindWebsites([Stock(stockId)], CancellationToken.None);

        results[stockId].Should().Be("www.later-chunk.com");
    }

    [Fact]
    public async Task StocksWithoutFilingsOrDisclosures_AreAbsentFromTheResult()
    {
        var options = NewDbOptions();
        var noFilings = Guid.NewGuid();
        var noDisclosure = Guid.NewGuid();
        await SeedFiling(
            options,
            noDisclosure,
            DocumentType.TenK,
            new DateOnly(2025, 9, 27),
            "This filing never mentions an address of any kind."
        );

        var results = await BuildSut(options)
            .FindWebsites([Stock(noFilings), Stock(noDisclosure)], CancellationToken.None);

        results.Should().BeEmpty();
    }
}
