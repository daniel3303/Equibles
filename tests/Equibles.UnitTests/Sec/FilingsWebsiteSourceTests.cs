using System.Text;
using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Media.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract: <c>FilingsWebsiteSource</c> reads the website disclosure from the
/// stock's most recent stored filing, trying 10-K first (where the disclosure is
/// mandated), then DEF 14A, then 10-Q — and reads the most recent filing of a
/// type, since a company's website can change over its filing history.
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
                new DocumentOnlyModuleConfiguration(),
                new MediaModuleConfiguration(),
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
        return new FilingsWebsiteSource(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<FilingsWebsiteSource>>()
        );
    }

    private static async Task SeedFiling(
        DbContextOptions<EquiblesFinancialDbContext> options,
        Guid stockId,
        DocumentType type,
        DateOnly reportingDate,
        string text
    )
    {
        using var ctx = NewContext(options);
        // GetWithContent eagerly includes the CommonStock principal, so the
        // document's stock must exist for the row to materialise.
        if (!await ctx.Set<CommonStock>().AnyAsync(cs => cs.Id == stockId))
            ctx.Set<CommonStock>()
                .Add(
                    new CommonStock
                    {
                        Id = stockId,
                        Ticker = stockId.ToString("N")[..8],
                        Cik = stockId.ToString("N")[..10],
                        Name = "Seeded Stock",
                    }
                );
        ctx.Set<Document>()
            .Add(
                new Document
                {
                    CommonStockId = stockId,
                    DocumentType = type,
                    ReportingDate = reportingDate,
                    Content = new File
                    {
                        Name = "filing",
                        Extension = "txt",
                        ContentType = "text/plain",
                        FileContent = { Bytes = Encoding.UTF8.GetBytes(text) },
                    },
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
