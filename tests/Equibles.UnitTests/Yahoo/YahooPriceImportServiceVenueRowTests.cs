using System.Reflection;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.TestSupport;
using Equibles.Worker;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Data.Prices;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins the Yahoo lane's side of venue-row ownership in the shared price store. A venue-derived
/// bar (SourceTicker `MIC:ISIN`) is never deleted by a series replacement unless the restated
/// series moved it onto another split basis, is never resettled from the feed, and never counts
/// as stored Yahoo history or as the feed's own latest date, so the deep backfill still runs
/// after a venue wrote first and the fetch window stays open while the venue runs ahead. The
/// unique (listing, date) index means a retained venue row's date must leave the insert batch.
/// </summary>
public class YahooPriceImportServiceVenueRowTests
{
    private const string Ticker = "AAPL";
    private const string VenueKey = "XNAS:US0378331005";
    private static readonly DateOnly Floor = new(2020, 1, 1);
    private static readonly DateOnly Today = new(2026, 6, 30);

    private static readonly MethodInfo ReplaceLockedPriceRows =
        typeof(YahooPriceImportService).GetMethod(
            "ReplaceLockedPriceRows",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static readonly MethodInfo HasStoredSeries = typeof(YahooPriceImportService).GetMethod(
        "HasStoredSeries",
        BindingFlags.NonPublic | BindingFlags.Instance
    );

    private static readonly MethodInfo ResettleStoredBars =
        typeof(YahooPriceImportService).GetMethod(
            "ResettleStoredBars",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

    private static readonly MethodInfo GetSyncStartDate = typeof(YahooPriceImportService).GetMethod(
        "GetSyncStartDate",
        BindingFlags.NonPublic | BindingFlags.Instance
    );

    [Fact]
    public async Task Replace_SameBasisVenueRow_KeepsItWithTheFreshAdjustedCloseAndSkipsItsInsert()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        db.AddRange(
            Row(db, stockId, new DateOnly(2024, 1, 2), 400m),
            Row(db, stockId, new DateOnly(2024, 1, 3), 40.2m, VenueKey, volume: 5_000)
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var fresh = new List<EquityDailyStockPrice>
        {
            Row(db, stockId, new DateOnly(2024, 1, 2), 40m, adjustedClose: 39m),
            Row(db, stockId, new DateOnly(2024, 1, 3), 40.4m, adjustedClose: 39.5m),
        };

        var outcome = await Replace(db, stockId, fresh);

        outcome.Should().Be(new VenueRowReconciliation(Retained: 1, Rebased: 0, Unrefreshed: 0));
        db.ChangeTracker.Clear();
        var stored = await StoredRows(db, stockId);
        stored.Should().HaveCount(2);
        stored[0].Close.Should().Be(40m);
        stored[0].SourceTicker.Should().Be(Ticker);
        var venue = stored[1];
        venue.SourceTicker.Should().Be(VenueKey);
        venue.Close.Should().Be(40.2m);
        venue.Volume.Should().Be(5_000);
        venue.AdjustedClose.Should().Be(39.5m);
    }

    [Fact]
    public async Task Replace_VenueRowOnAnotherBasis_IsDeletedForTheRestatedRow()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        // The venue captured the session as traded (402); the restated series is 10:1 adjusted.
        db.Add(Row(db, stockId, new DateOnly(2024, 1, 3), 402m, VenueKey));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var fresh = new List<EquityDailyStockPrice>
        {
            Row(db, stockId, new DateOnly(2024, 1, 3), 40.2m),
        };

        var outcome = await Replace(db, stockId, fresh);

        outcome.Should().Be(new VenueRowReconciliation(Retained: 0, Rebased: 1, Unrefreshed: 0));
        db.ChangeTracker.Clear();
        EquityDailyStockPrice stored = (await StoredRows(db, stockId)).Single();
        stored.SourceTicker.Should().Be(Ticker);
        stored.Close.Should().Be(40.2m);
    }

    [Fact]
    public async Task Replace_VenueRowOnADateTheSeriesDidNotServe_IsKeptAndCounted()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        db.AddRange(
            Row(db, stockId, new DateOnly(2024, 1, 2), 400m),
            Row(db, stockId, new DateOnly(2024, 1, 5), 41m, VenueKey)
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var fresh = new List<EquityDailyStockPrice>
        {
            Row(db, stockId, new DateOnly(2024, 1, 2), 40m),
        };

        var outcome = await Replace(db, stockId, fresh);

        outcome.Should().Be(new VenueRowReconciliation(Retained: 0, Rebased: 0, Unrefreshed: 1));
        db.ChangeTracker.Clear();
        var stored = await StoredRows(db, stockId);
        stored.Select(p => p.SourceTicker).Should().Equal(Ticker, VenueKey);
        stored[1].Close.Should().Be(41m);
    }

    [Fact]
    public async Task Replace_VenueRowOutsideTheWindow_IsNeitherTouchedNorCounted()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        db.AddRange(
            Row(db, stockId, new DateOnly(2019, 6, 3), 10m, VenueKey),
            Row(db, stockId, new DateOnly(2024, 1, 2), 400m)
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var fresh = new List<EquityDailyStockPrice>
        {
            Row(db, stockId, new DateOnly(2024, 1, 2), 40m),
        };

        var outcome = await Replace(db, stockId, fresh);

        outcome.Should().Be(new VenueRowReconciliation(Retained: 0, Rebased: 0, Unrefreshed: 0));
        db.ChangeTracker.Clear();
        var stored = await StoredRows(db, stockId);
        stored.Select(p => p.Close).Should().Equal(10m, 40m);
    }

    [Fact]
    public async Task HasStoredSeries_IgnoresVenueRows()
    {
        var (sut, options, stockId) = await BuildSutWithStock();
        await using (var seed = NewContext(options))
        {
            seed.Add(Row(seed, stockId, new DateOnly(2026, 6, 1), 40m, VenueKey));
            await seed.SaveChangesAsync();
        }

        var withVenueOnly = await Invoke<bool>(HasStoredSeries, sut, Target(options, stockId));

        await using (var seed = NewContext(options))
        {
            seed.Add(Row(seed, stockId, new DateOnly(2026, 6, 2), 40m));
            await seed.SaveChangesAsync();
        }

        var withYahooRow = await Invoke<bool>(HasStoredSeries, sut, Target(options, stockId));

        withVenueOnly.Should().BeFalse();
        withYahooRow.Should().BeTrue();
    }

    [Fact]
    public async Task SyncStartDate_IgnoresVenueRows_SoAVenueBarAheadOfTheFeedKeepsTheWindowOpen()
    {
        // A venue bar lands on the session's own UTC date, one day before the feed admits that
        // session, so counting it would put the start date at today on every cycle and the
        // listing would never be fetched again.
        var (sut, options, stockId) = await BuildSutWithStock();
        var empty = await Invoke<DateOnly>(GetSyncStartDate, sut, Target(options, stockId));

        await using (var seed = NewContext(options))
        {
            seed.Add(Row(seed, stockId, Today.AddDays(-1), 41m, VenueKey));
            await seed.SaveChangesAsync();
        }
        var venueOnly = await Invoke<DateOnly>(GetSyncStartDate, sut, Target(options, stockId));

        await using (var seed = NewContext(options))
        {
            seed.Add(Row(seed, stockId, Today.AddDays(-4), 40m));
            await seed.SaveChangesAsync();
        }
        var behindTheVenue = await Invoke<DateOnly>(
            GetSyncStartDate,
            sut,
            Target(options, stockId)
        );

        venueOnly.Should().Be(empty, "a venue bar is not stored Yahoo history");
        behindTheVenue.Should().Be(Today.AddDays(-3), "the feed resumes after its own last bar");
    }

    [Fact]
    public async Task Resettle_CorrectsTheYahooBarAndLeavesTheVenueBarAlone()
    {
        var (sut, options, stockId) = await BuildSutWithStock();
        var yahooDate = Today.AddDays(-2);
        var venueDate = Today.AddDays(-1);
        await using (var seed = NewContext(options))
        {
            seed.AddRange(
                Row(seed, stockId, yahooDate, 40m, volume: 1_000),
                Row(seed, stockId, venueDate, 41m, VenueKey, volume: 1_000)
            );
            await seed.SaveChangesAsync();
        }
        List<EquityDailyStockPrice> fetched;
        await using (var probe = NewContext(options))
        {
            fetched =
            [
                Row(probe, stockId, yahooDate, 40.1m, volume: 2_000),
                Row(probe, stockId, venueDate, 41.5m, volume: 9_000),
            ];
        }

        var corrected = await (Task<int>)
            ResettleStoredBars.Invoke(
                sut,
                [Target(options, stockId), fetched, Today, CancellationToken.None]
            )!;

        corrected.Should().Be(1);
        await using var verify = NewContext(options);
        var stored = await verify
            .Set<EquityDailyStockPrice>()
            .Where(p => p.Listing.Security.EquityIssuerId == stockId)
            .OrderBy(p => p.Date)
            .ToListAsync();
        stored[0].Close.Should().Be(40.1m);
        stored[0].Volume.Should().Be(2_000);
        stored[1].SourceTicker.Should().Be(VenueKey);
        stored[1].Close.Should().Be(41m);
        stored[1].Volume.Should().Be(1_000);
    }

    private static async Task<VenueRowReconciliation> Replace(
        EquiblesFinancialDbContext db,
        Guid stockId,
        List<EquityDailyStockPrice> fresh
    )
    {
        var stockRepo = new EquityIssuerRepository(db);
        EquityIssuer stock = await stockRepo.GetAll().SingleAsync(s => s.Id == stockId);
        var target = new PriceSeriesTarget(
            Ticker,
            stockId,
            stock.Presentation.EquityListingId,
            IsPrimary: true
        );
        return await (Task<VenueRowReconciliation>)
            ReplaceLockedPriceRows.Invoke(
                null,
                [
                    new EquityDailyStockPriceRepository(db),
                    stockRepo,
                    target,
                    Floor,
                    Today,
                    fresh,
                    new LockedPriceSeries(stock, IsPrimary: true),
                    null,
                    CancellationToken.None,
                ]
            )!;
    }

    private static async Task<T> Invoke<T>(
        MethodInfo method,
        YahooPriceImportService sut,
        PriceSeriesTarget target
    ) => await (Task<T>)method.Invoke(sut, [target, CancellationToken.None])!;

    private static PriceSeriesTarget Target(
        DbContextOptions<EquiblesFinancialDbContext> options,
        Guid stockId
    )
    {
        using var db = NewContext(options);
        var listingId = db.Set<EquityListing>()
            .Where(l => l.Security.EquityIssuerId == stockId && l.Ticker == Ticker)
            .Select(l => l.Id)
            .Single();
        return new PriceSeriesTarget(Ticker, stockId, listingId, IsPrimary: true);
    }

    private static async Task<(
        YahooPriceImportService Sut,
        DbContextOptions<EquiblesFinancialDbContext> Options,
        Guid StockId
    )> BuildSutWithStock()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var stockId = Guid.NewGuid();
        await using (var seed = NewContext(options))
        {
            seed.Add(Stock(stockId));
            await seed.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(options));
        services.AddScoped<EquityIssuerRepository>();
        services.AddScoped<EquityDailyStockPriceRepository>();
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var sut = new YahooPriceImportService(
            scopeFactory,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            Substitute.For<IYahooFinanceClient>(),
            new TickerMapService(scopeFactory),
            new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>()),
            Options.Create(new WorkerOptions()),
            Options.Create(new YahooPriceScraperOptions())
        );
        return (sut, options, stockId);
    }

    private static Task<List<EquityDailyStockPrice>> StoredRows(
        EquiblesFinancialDbContext db,
        Guid stockId
    ) =>
        db.Set<EquityDailyStockPrice>()
            .Where(p => p.Listing.Security.EquityIssuerId == stockId)
            .OrderBy(p => p.Date)
            .ToListAsync();

    private static EquityIssuer Stock(Guid stockId) =>
        EquityIssuerSeed.Create(Id: stockId, Ticker: Ticker);

    private static EquityDailyStockPrice Row(
        EquiblesFinancialDbContext db,
        Guid stockId,
        DateOnly date,
        decimal close,
        string sourceTicker = Ticker,
        long volume = 1_000,
        decimal? adjustedClose = null
    )
    {
        EquityListing listing = NativeListingSeed.ForStockId(db, stockId, Ticker);
        return new EquityDailyStockPrice
        {
            Listing = listing,
            EquityListingId = listing.Id,
            SourceTicker = sourceTicker,
            Date = date,
            Open = close,
            High = close,
            Low = close,
            Close = close,
            AdjustedClose = adjustedClose ?? close,
            Volume = volume,
        };
    }

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return NewContext(options);
    }

    private static EquiblesFinancialDbContext NewContext(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new YahooModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }
}
