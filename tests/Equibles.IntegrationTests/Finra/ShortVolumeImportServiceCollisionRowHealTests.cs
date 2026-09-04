using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Calendars;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Configuration;
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
/// End-to-end pin of the case-fold collision heal. The v1 importer's case-insensitive
/// ticker map folded FINRA's lowercase preferred symbols onto common tickers, so a stock
/// could hold a stored row for a day its own symbol never traded. The v2 re-import must
/// DELETE such a row (no aggregate exists to overwrite it), must leave rows alone when the
/// file simply doesn't mention the ticker, and must not be blocked by v1 partition markers
/// (the dataset-key bump orphans them, which is the heal's re-import lever).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortVolumeImportServiceCollisionRowHealTests : ParadeDbMcpTestBase
{
    public ShortVolumeImportServiceCollisionRowHealTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private CommonStock _stock;
    private DateOnly _corruptDate;

    private async Task SeedStockAndCorruptRow()
    {
        _stock = new CommonStock
        {
            Cik = "0000000778",
            Ticker = "TESTW",
            Name = "Collision Heal Test Inc.",
        };
        _corruptDate = PreviousTradingDay(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        DbContext.Add(_stock);
        // The collision artifact: a row for a day whose file (stubbed below) only ever
        // carried the lowercase sibling security's symbol.
        DbContext.Add(
            new DailyShortVolume
            {
                CommonStockId = _stock.Id,
                ListedTicker = _stock.Ticker,
                Date = _corruptDate,
                ShortVolume = 5_000,
                TotalVolume = 9_000,
            }
        );
        // A v1 marker for the same day: the dataset bump must orphan it so the day
        // re-imports at all.
        DbContext.Add(
            new FinraImportPartition
            {
                Dataset = "daily-short-volume-files-v1",
                PartitionDate = _corruptDate,
                ScopeKey = "all",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private static DateOnly PreviousTradingDay(DateOnly date)
    {
        while (!UsMarketCalendar.IsTradingDay(date))
            date = date.AddDays(-1);
        return date;
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
            Options.Create(
                new WorkerOptions { TickersToSync = [], MinSyncDate = DateTime.UtcNow.AddDays(-7) }
            ),
            Options.Create(new FinraScraperOptions()),
            new FinraImportPartitionTracker(new FinraImportPartitionRepository(DbContext)),
            TimeProvider.System
        );
    }

    [Fact]
    public async Task Import_FileCarriesOnlyTheCaseVariant_DeletesTheCollisionRow()
    {
        await SeedStockAndCorruptRow();

        var finraClient = Substitute.For<IFinraClient>();
        finraClient
            .GetDailyShortVolume(Arg.Any<DateOnly>())
            .Returns(
                new List<ShortVolumeRecord>
                {
                    // Only the sibling security's lowercase symbol — under the ordinal map
                    // it maps to nothing, so the stored row is a collision artifact.
                    new()
                    {
                        Symbol = "TESTw",
                        ShortVolume = 5_000,
                        TotalVolume = 9_000,
                    },
                }
            );

        await BuildService(finraClient).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var remaining = await verify
            .Set<DailyShortVolume>()
            .AsNoTracking()
            .AnyAsync(v => v.CommonStockId == _stock.Id && v.Date == _corruptDate);
        remaining.Should().BeFalse("the row's only source was the case-folded sibling security");
    }

    [Fact]
    public async Task Import_FileDoesNotMentionTheTicker_KeepsTheStoredRow()
    {
        await SeedStockAndCorruptRow();

        var finraClient = Substitute.For<IFinraClient>();
        finraClient
            .GetDailyShortVolume(Arg.Any<DateOnly>())
            .Returns(
                new List<ShortVolumeRecord>
                {
                    new()
                    {
                        Symbol = "OTHER",
                        ShortVolume = 1_000,
                        TotalVolume = 2_000,
                    },
                }
            );

        await BuildService(finraClient).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var remaining = await verify
            .Set<DailyShortVolume>()
            .AsNoTracking()
            .AnyAsync(v => v.CommonStockId == _stock.Id && v.Date == _corruptDate);
        remaining
            .Should()
            .BeTrue("absence from one day's file is not evidence the row was a collision");
    }

    [Fact]
    public async Task Import_FileCarriesTheExactSymbol_OverwritesInsteadOfDeleting()
    {
        await SeedStockAndCorruptRow();

        var finraClient = Substitute.For<IFinraClient>();
        finraClient
            .GetDailyShortVolume(Arg.Any<DateOnly>())
            .Returns(
                new List<ShortVolumeRecord>
                {
                    new()
                    {
                        Symbol = "TESTW",
                        ShortVolume = 90_492,
                        TotalVolume = 117_265,
                    },
                    new()
                    {
                        Symbol = "TESTw",
                        ShortVolume = 5_000,
                        TotalVolume = 9_000,
                    },
                }
            );

        await BuildService(finraClient).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var row = await verify
            .Set<DailyShortVolume>()
            .AsNoTracking()
            .SingleAsync(v => v.CommonStockId == _stock.Id && v.Date == _corruptDate);
        row.ShortVolume.Should().Be(90_492, "the upsert replaces the corrupted sum");
        row.TotalVolume.Should().Be(117_265);
    }
}
