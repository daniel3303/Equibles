using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// <see cref="ShortVolumeImportService.Import"/> was almost entirely uncovered —
/// only the up-to-date early return. These drive the real day-by-day import
/// loop end-to-end: a seeded "latest" row makes the window the last few days,
/// a stubbed FINRA client returns records that exercise the per-stock
/// aggregation (new + merge), the unknown/empty-symbol skips, and the batch
/// persister; a second test drives the HttpRequestException catch arm.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortVolumeImportServicePipelineTests : ParadeDbMcpTestBase
{
    public ShortVolumeImportServicePipelineTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private CommonStock _stock;

    private async Task SeedStockAndLatestRow()
    {
        _stock = new CommonStock
        {
            Cik = "0000000777",
            Ticker = "TESTV",
            Name = "Short Volume Test Inc.",
        };
        DbContext.Add(_stock);
        // A "latest" row 3 days back bounds the loop to the last 3 days.
        DbContext.Add(
            new DailyShortVolume
            {
                CommonStockId = _stock.Id,
                Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3),
                ShortVolume = 1,
                TotalVolume = 1,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private ShortVolumeImportService BuildService(IFinraClient finraClient)
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(DbContext)),
            (typeof(DailyShortVolumeRepository), new DailyShortVolumeRepository(DbContext))
        );
        return new ShortVolumeImportService(
            scopeFactory,
            Substitute.For<ILogger<ShortVolumeImportService>>(),
            finraClient,
            new TickerMapService(scopeFactory),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions { TickersToSync = [] })
        );
    }

    [Fact]
    public async Task Import_RecordsAcrossMarkets_AggregatesPerStockAndPersists()
    {
        await SeedStockAndLatestRow();

        var finraClient = Substitute.For<IFinraClient>();
        finraClient
            .GetDailyShortVolume(Arg.Any<DateOnly>())
            .Returns(
                new List<ShortVolumeRecord>
                {
                    // Two rows for the tracked symbol (different markets) must
                    // aggregate into one row: 100+50 short, 200+60 total.
                    new()
                    {
                        Symbol = "TESTV",
                        ShortVolume = 100,
                        TotalVolume = 200,
                    },
                    new()
                    {
                        Symbol = "TESTV",
                        ShortVolume = 50,
                        TotalVolume = 60,
                    },
                    // Unknown symbol → skipped (not in ticker map).
                    new()
                    {
                        Symbol = "NOPE",
                        ShortVolume = 9,
                        TotalVolume = 9,
                    },
                    // Empty symbol → skipped.
                    new() { Symbol = "", ShortVolume = 7 },
                }
            );

        await BuildService(finraClient).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var aggregated = await verify
            .Set<DailyShortVolume>()
            .AsNoTracking()
            .Where(v => v.CommonStockId == _stock.Id && v.ShortVolume == 150)
            .ToListAsync();
        aggregated.Should().NotBeEmpty("the two per-market rows must aggregate to ShortVolume 150");
        aggregated.Should().OnlyContain(v => v.TotalVolume == 260);
    }

    [Fact]
    public async Task Import_FinraClientThrowsHttpRequestException_SkipsDateWithoutThrowing()
    {
        await SeedStockAndLatestRow();

        var finraClient = Substitute.For<IFinraClient>();
        finraClient
            .GetDailyShortVolume(Arg.Any<DateOnly>())
            .Returns<List<ShortVolumeRecord>>(_ => throw new HttpRequestException("FINRA down"));

        await BuildService(finraClient).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var hasAggregate = await verify
            .Set<DailyShortVolume>()
            .AsNoTracking()
            .AnyAsync(v => v.CommonStockId == _stock.Id && v.ShortVolume == 150);
        hasAggregate.Should().BeFalse("every fetch failed, so nothing new was persisted");
    }
}
