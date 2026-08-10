using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.IntegrationTests.Yahoo;

[Collection(ParadeDbCollection.Name)]
public class ListedPriceRollingCompatibilityTests : IAsyncLifetime
{
    private static readonly MethodInfo ReplacePriceRowsMethod =
        typeof(YahooPriceImportService).GetMethod(
            "ReplacePriceRows",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private readonly ParadeDbFixture _fixture;

    public ListedPriceRollingCompatibilityTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RetiringWorker_ReaderAndWriterCannotSeeExactListedSeries()
    {
        var stockId = Guid.NewGuid();
        var primaryId = Guid.NewGuid();
        var secondaryId = Guid.NewGuid();
        var legacyId = Guid.NewGuid();
        await using (var current = _fixture.CreateDbContext())
        {
            current.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "GOOGL",
                    SecondaryTickers = ["GOOG"],
                }
            );
            current.AddRange(
                Price(
                    primaryId,
                    stockId,
                    new DateOnly(2026, 8, 3),
                    close: 200m,
                    listedTicker: "GOOGL"
                ),
                Price(
                    secondaryId,
                    stockId,
                    new DateOnly(2026, 8, 4),
                    close: 600_000m,
                    listedTicker: "GOOG"
                )
            );
            await current.SaveChangesAsync();
        }

        await using (var retiring = CreateRetiringWorkerContext())
        {
            retiring.Prices.Add(
                LegacyPrice(legacyId, stockId, new DateOnly(2026, 8, 4), close: 201m)
            );
            await retiring.SaveChangesAsync();
        }

        await using (var retiring = CreateRetiringWorkerContext())
        {
            var visible = await retiring
                .Prices.Where(price => price.CommonStockId == stockId)
                .OrderByDescending(price => price.Date)
                .ToListAsync();

            visible.Should().ContainSingle();
            visible.Single().Id.Should().Be(legacyId);
            visible.Single().Close.Should().Be(201m);

            var legacy = visible.Single();
            legacy.Close = 202m;
            await retiring.SaveChangesAsync();
        }

        await using (var verification = _fixture.CreateDbContext())
        {
            var exact = await verification
                .Set<DailyStockPrice>()
                .Where(price => price.CommonStockId == stockId)
                .OrderBy(price => price.ListedTicker)
                .ToListAsync();

            exact.Should().HaveCount(2);
            exact.Single(price => price.Id == primaryId).Close.Should().Be(200m);
            exact.Single(price => price.Id == secondaryId).Close.Should().Be(600_000m);
            exact.Should().NotContain(price => price.Id == legacyId);
        }

        await using (var retiring = CreateRetiringWorkerContext())
        {
            var legacy = await retiring.Prices.SingleAsync(price => price.Id == legacyId);
            legacy.Close.Should().Be(202m);
            retiring.Prices.Remove(legacy);
            await retiring.SaveChangesAsync();
        }

        await using (var verification = _fixture.CreateDbContext())
        {
            var exactIds = await verification
                .Set<DailyStockPrice>()
                .Select(price => price.Id)
                .ToListAsync();
            exactIds.Should().BeEquivalentTo([primaryId, secondaryId]);
        }
    }

    [Fact]
    public async Task CurrentReader_DoesNotGuessAnUnlabeledLegacyRowIntoThePrimarySeries()
    {
        var stockId = Guid.NewGuid();
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "GOOGL",
                    SecondaryTickers = ["GOOG"],
                }
            );
            await seed.SaveChangesAsync();
        }

        await using (var retiring = CreateRetiringWorkerContext())
        {
            retiring.Prices.Add(
                LegacyPrice(Guid.NewGuid(), stockId, new DateOnly(2026, 8, 3), close: 190m)
            );
            await retiring.SaveChangesAsync();
        }

        await using var current = _fixture.CreateDbContext();
        var stock = await current.Set<CommonStock>().SingleAsync(s => s.Id == stockId);
        var repository = new DailyStockPriceRepository(current);
        (await repository.GetByStock(stock).ToListAsync()).Should().BeEmpty();
        (await repository.GetByStock(stock, "GOOGL").ToListAsync()).Should().BeEmpty();
        (await repository.GetByStock(stock, "GOOG").ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task InitialExactSeries_IsInvisibleUntilEveryInsertBatchCommits()
    {
        var stockId = Guid.NewGuid();
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(new CommonStock { Id = stockId, Ticker = "AAPL" });
            await seed.SaveChangesAsync();
        }

        var firstDate = new DateOnly(2024, 1, 1);
        var freshRows = Enumerable
            .Range(0, 501)
            .Select(offset =>
                Price(
                    Guid.NewGuid(),
                    stockId,
                    firstDate.AddDays(offset),
                    close: 100m + offset,
                    listedTicker: "AAPL"
                )
            )
            .ToList();
        var saveGate = new FirstPriceBatchSaveGate();
        await using var writer = _fixture.CreateDbContext(options =>
            options.AddInterceptors(saveGate)
        );
        var writeTask =
            (Task<bool>)
                ReplacePriceRowsMethod.Invoke(
                    null,
                    [
                        new DailyStockPriceRepository(writer),
                        new CommonStockRepository(writer),
                        new PriceSeriesTarget("AAPL", stockId, IsPrimary: true),
                        firstDate,
                        firstDate.AddDays(501),
                        freshRows,
                        CancellationToken.None,
                    ]
                );

        await saveGate.WaitUntilFirstBatchSaved();
        await using (var concurrentReader = _fixture.CreateDbContext())
        {
            var visible = await concurrentReader
                .Set<DailyStockPrice>()
                .CountAsync(price => price.CommonStockId == stockId);
            visible
                .Should()
                .Be(0, "the first batch remains hidden inside the uncommitted transaction");
        }

        saveGate.Release();
        (await writeTask.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();

        await using var verification = _fixture.CreateDbContext();
        var committed = await verification
            .Set<DailyStockPrice>()
            .CountAsync(price => price.CommonStockId == stockId);
        committed.Should().Be(501);
    }

    [Fact]
    public async Task RetiringWorker_StaleSplitRevisionAfterStamp_RemainsPending()
    {
        var stockId = Guid.NewGuid();
        var splitId = Guid.NewGuid();
        var effectiveDate = new DateOnly(2026, 7, 15);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "GOOGL",
                    SecondaryTickers = ["GOOG"],
                }
            );
            seed.Add(
                new StockSplit
                {
                    Id = splitId,
                    CommonStockId = stockId,
                    PriceSeriesTicker = "GOOGL",
                    EffectiveDate = effectiveDate,
                    Numerator = 2m,
                    Denominator = 1m,
                    Source = StockSplitSource.Yahoo,
                    PriceAdjustmentAppliedTime = null,
                }
            );
            await seed.SaveChangesAsync();
        }

        PendingPriceReconciliationSeries selected;
        await using (var selection = _fixture.CreateDbContext())
        {
            var manager = new CorporateActionPriceReconciliationManager(
                new StockSplitRepository(selection),
                new CashDividendRepository(selection),
                new CommonStockRepository(selection),
                new CorporateActionPriceReconciliationCursorRepository(selection)
            );
            selected = (
                await manager.SelectPendingSeries(50, DateOnly.FromDateTime(DateTime.UtcNow))
            ).Series.Single();
        }

        var saveGate = new SplitStampSaveGate();
        await using var stamping = _fixture.CreateDbContext(options =>
            options.AddInterceptors(saveGate)
        );
        var stampingManager = new CorporateActionPriceReconciliationManager(
            new StockSplitRepository(stamping),
            new CashDividendRepository(stamping),
            new CommonStockRepository(stamping),
            new CorporateActionPriceReconciliationCursorRepository(stamping)
        );
        var appliedTime = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
        var stampTask = stampingManager.StampApplied(selected, appliedTime);
        await saveGate.WaitUntilSaving();

        await using var retiring = CreateRetiringWorkerContext();
        var staleSplit = await retiring.Splits.SingleAsync(split => split.Id == splitId);
        staleSplit.PriceAdjustmentAppliedTime.Should().BeNull();
        staleSplit.Numerator = 3m;
        staleSplit.PriceAdjustmentAppliedTime = null;
        retiring.Entry(staleSplit).Property(split => split.Numerator).IsModified.Should().BeTrue();
        retiring
            .Entry(staleSplit)
            .Property(split => split.PriceAdjustmentAppliedTime)
            .IsModified.Should()
            .BeFalse("the retiring model omits a marker it loaded as null from its UPDATE");

        var retiringSave = retiring.SaveChangesAsync();
        var completedWhileLocked =
            await Task.WhenAny(retiringSave, Task.Delay(TimeSpan.FromMilliseconds(300)))
            == retiringSave;

        saveGate.Release();
        var stamped = await stampTask.WaitAsync(TimeSpan.FromSeconds(5));
        await retiringSave.WaitAsync(TimeSpan.FromSeconds(5));

        completedWhileLocked
            .Should()
            .BeFalse("the current writer holds a row lock through its stamp transaction");
        stamped.Should().Be(1);

        await using var verification = _fixture.CreateDbContext();
        var revised = await verification
            .Set<StockSplit>()
            .SingleAsync(split => split.Id == splitId);
        revised.Numerator.Should().Be(3m);
        revised
            .PriceAdjustmentAppliedTime.Should()
            .BeNull("the database invalidates a revision made by the stale retiring model");
    }

    private RetiringWorkerContext CreateRetiringWorkerContext()
    {
        var options = new DbContextOptionsBuilder<RetiringWorkerContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new RetiringWorkerContext(options);
    }

    private static DailyStockPrice Price(
        Guid id,
        Guid stockId,
        DateOnly date,
        decimal close,
        string listedTicker
    ) =>
        new()
        {
            Id = id,
            CommonStockId = stockId,
            ListedTicker = listedTicker,
            Date = date,
            Open = close - 1m,
            High = close + 1m,
            Low = close - 2m,
            Close = close,
            AdjustedClose = close,
            Volume = 1_000,
            CreationTime = DateTime.UtcNow,
        };

    private static LegacyPriceRow LegacyPrice(
        Guid id,
        Guid stockId,
        DateOnly date,
        decimal close
    ) =>
        new()
        {
            Id = id,
            CommonStockId = stockId,
            Date = date,
            Open = close - 1m,
            High = close + 1m,
            Low = close - 2m,
            Close = close,
            AdjustedClose = close,
            Volume = 1_000,
            CreationTime = DateTime.UtcNow,
        };

    private sealed class RetiringWorkerContext : DbContext
    {
        public RetiringWorkerContext(DbContextOptions<RetiringWorkerContext> options)
            : base(options) { }

        public DbSet<LegacyPriceRow> Prices => Set<LegacyPriceRow>();
        public DbSet<LegacyStockSplitRow> Splits => Set<LegacyStockSplitRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LegacyPriceRow>(price =>
            {
                price.ToTable("DailyStockPrice");
                price.HasKey(row => row.Id);
            });
            modelBuilder.Entity<LegacyStockSplitRow>(split =>
            {
                split.ToTable("StockSplit");
                split.HasKey(row => row.Id);
            });
        }
    }

    private sealed class LegacyPriceRow
    {
        public Guid Id { get; set; }
        public Guid CommonStockId { get; set; }
        public DateOnly Date { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal AdjustedClose { get; set; }
        public long Volume { get; set; }
        public DateTime CreationTime { get; set; }
    }

    private sealed class LegacyStockSplitRow
    {
        public Guid Id { get; set; }
        public Guid CommonStockId { get; set; }
        public DateOnly EffectiveDate { get; set; }
        public decimal Numerator { get; set; }
        public decimal Denominator { get; set; }
        public string Source { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime? PriceAdjustmentAppliedTime { get; set; }
    }

    private sealed class SplitStampSaveGate : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> _saving = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task WaitUntilSaving() => _saving.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release() => _release.TrySetResult(true);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            _saving.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class FirstPriceBatchSaveGate : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> _firstBatchSaved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _savedCalls;

        public Task WaitUntilFirstBatchSaved() =>
            _firstBatchSaved.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Release() => _release.TrySetResult(true);

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref _savedCalls) == 1)
            {
                _firstBatchSaved.TrySetResult(true);
                await _release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
