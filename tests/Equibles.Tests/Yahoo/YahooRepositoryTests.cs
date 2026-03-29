using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Tests.Helpers;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Yahoo;

public class DailyStockPriceRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly DailyStockPriceRepository _repository;
    private readonly CommonStock _apple;
    private readonly CommonStock _microsoft;

    public DailyStockPriceRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _repository = new DailyStockPriceRepository(_dbContext);

        _apple = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL", Name = "Apple Inc." };
        _microsoft = new CommonStock { Id = Guid.NewGuid(), Ticker = "MSFT", Name = "Microsoft Corp." };
        _dbContext.Set<CommonStock>().AddRange(_apple, _microsoft);
        _dbContext.SaveChanges();
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private DailyStockPrice CreatePrice(
        CommonStock stock, DateOnly date,
        decimal close = 150m, decimal open = 148m,
        decimal high = 152m, decimal low = 147m, long volume = 1_000_000
    ) {
        return new DailyStockPrice {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            Date = date,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            AdjustedClose = close,
            Volume = volume,
        };
    }

    private async Task SeedPrices(params DailyStockPrice[] prices) {
        _dbContext.Set<DailyStockPrice>().AddRange(prices);
        await _dbContext.SaveChangesAsync();
    }

    // ── GetByStock (all prices) ────────────────────────────────────────

    [Fact]
    public async Task GetByStock_ReturnsPricesForRequestedStock() {
        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 1, 2), close: 180m),
            CreatePrice(_apple, new DateOnly(2026, 1, 3), close: 182m),
            CreatePrice(_microsoft, new DateOnly(2026, 1, 2), close: 400m)
        );

        var result = await _repository.GetByStock(_apple).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(p => p.CommonStockId.Should().Be(_apple.Id));
    }

    [Fact]
    public async Task GetByStock_ReturnsEmpty_WhenStockHasNoPrices() {
        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 1, 2), close: 180m)
        );

        var result = await _repository.GetByStock(_microsoft).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByStock_DoesNotReturnPricesFromOtherStocks() {
        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 1, 2), close: 180m),
            CreatePrice(_microsoft, new DateOnly(2026, 1, 2), close: 400m),
            CreatePrice(_microsoft, new DateOnly(2026, 1, 3), close: 405m)
        );

        var result = await _repository.GetByStock(_apple).ToListAsync();

        result.Should().ContainSingle();
        result[0].Close.Should().Be(180m);
    }

    // ── GetByStock (date range) ────────────────────────────────────────

    [Fact]
    public async Task GetByStock_WithDateRange_FiltersCorrectly() {
        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 1, 1), close: 175m),
            CreatePrice(_apple, new DateOnly(2026, 1, 5), close: 180m),
            CreatePrice(_apple, new DateOnly(2026, 1, 10), close: 185m),
            CreatePrice(_apple, new DateOnly(2026, 1, 15), close: 190m)
        );

        var result = await _repository
            .GetByStock(_apple, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 10))
            .ToListAsync();

        result.Should().HaveCount(2);
        result.Select(p => p.Close).Should().BeEquivalentTo([180m, 185m]);
    }

    [Fact]
    public async Task GetByStock_WithDateRange_IncludesBoundaryDates() {
        var startDate = new DateOnly(2026, 3, 1);
        var endDate = new DateOnly(2026, 3, 31);

        await SeedPrices(
            CreatePrice(_apple, startDate, close: 170m),
            CreatePrice(_apple, endDate, close: 195m),
            CreatePrice(_apple, new DateOnly(2026, 2, 28), close: 165m),
            CreatePrice(_apple, new DateOnly(2026, 4, 1), close: 200m)
        );

        var result = await _repository
            .GetByStock(_apple, startDate, endDate)
            .ToListAsync();

        result.Should().HaveCount(2);
        result.Select(p => p.Date).Should().BeEquivalentTo([startDate, endDate]);
    }

    [Fact]
    public async Task GetByStock_WithDateRange_ReturnsEmpty_WhenNoPricesInRange() {
        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 1, 1), close: 175m),
            CreatePrice(_apple, new DateOnly(2026, 1, 31), close: 195m)
        );

        var result = await _repository
            .GetByStock(_apple, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30))
            .ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByStock_WithDateRange_ExcludesOtherStocks() {
        var date = new DateOnly(2026, 1, 5);

        await SeedPrices(
            CreatePrice(_apple, date, close: 180m),
            CreatePrice(_microsoft, date, close: 400m)
        );

        var result = await _repository
            .GetByStock(_apple, date, date)
            .ToListAsync();

        result.Should().ContainSingle()
            .Which.Close.Should().Be(180m);
    }

    // ── GetLatestDate ──────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestDate_ReturnsMostRecentDateForStock() {
        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 1, 1)),
            CreatePrice(_apple, new DateOnly(2026, 3, 15)),
            CreatePrice(_apple, new DateOnly(2026, 2, 10))
        );

        var result = await _repository.GetLatestDate(_apple).ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(new DateOnly(2026, 3, 15));
    }

    [Fact]
    public async Task GetLatestDate_ReturnsEmpty_WhenStockHasNoPrices() {
        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 1, 1))
        );

        var result = await _repository.GetLatestDate(_microsoft).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestDate_IgnoresDatesFromOtherStocks() {
        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 1, 10)),
            CreatePrice(_microsoft, new DateOnly(2026, 3, 20))
        );

        var result = await _repository.GetLatestDate(_apple).ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(new DateOnly(2026, 1, 10));
    }

    // ── GetLatestDateAcrossAllStocks ───────────────────────────────────

    [Fact]
    public async Task GetLatestDateAcrossAllStocks_ReturnsMostRecentDateOverall() {
        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 1, 10)),
            CreatePrice(_apple, new DateOnly(2026, 2, 5)),
            CreatePrice(_microsoft, new DateOnly(2026, 3, 1))
        );

        var result = await _repository.GetLatestDateAcrossAllStocks().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public async Task GetLatestDateAcrossAllStocks_ReturnsEmpty_WhenNoPricesExist() {
        var result = await _repository.GetLatestDateAcrossAllStocks().ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestDateAcrossAllStocks_ReturnsSingleDate_WhenMultipleStocksShareIt() {
        var latestDate = new DateOnly(2026, 3, 15);

        await SeedPrices(
            CreatePrice(_apple, latestDate),
            CreatePrice(_microsoft, latestDate),
            CreatePrice(_apple, new DateOnly(2026, 1, 1))
        );

        var result = await _repository.GetLatestDateAcrossAllStocks().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(latestDate);
    }
}

public class YahooStockPriceProviderTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly YahooStockPriceProvider _provider;
    private readonly CommonStock _apple;
    private readonly CommonStock _microsoft;
    private readonly CommonStock _google;

    public YahooStockPriceProviderTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _provider = new YahooStockPriceProvider(_dbContext);

        _apple = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL", Name = "Apple Inc." };
        _microsoft = new CommonStock { Id = Guid.NewGuid(), Ticker = "MSFT", Name = "Microsoft Corp." };
        _google = new CommonStock { Id = Guid.NewGuid(), Ticker = "GOOG", Name = "Alphabet Inc." };
        _dbContext.Set<CommonStock>().AddRange(_apple, _microsoft, _google);
        _dbContext.SaveChanges();
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private DailyStockPrice CreatePrice(CommonStock stock, DateOnly date, decimal close) {
        return new DailyStockPrice {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            Date = date,
            Open = close - 2m,
            High = close + 2m,
            Low = close - 3m,
            Close = close,
            AdjustedClose = close,
            Volume = 500_000,
        };
    }

    private async Task SeedPrices(params DailyStockPrice[] prices) {
        _dbContext.Set<DailyStockPrice>().AddRange(prices);
        await _dbContext.SaveChangesAsync();
    }

    // ── Exact date match ───────────────────────────────────────────────

    [Fact]
    public async Task GetClosingPrices_ReturnsPriceForExactDate() {
        var date = new DateOnly(2026, 3, 2);
        await SeedPrices(CreatePrice(_apple, date, 180m));

        var result = await _provider.GetClosingPrices([(_apple.Id, date)]);

        result.Should().ContainSingle();
        result[(_apple.Id, date)].Should().Be(180m);
    }

    [Fact]
    public async Task GetClosingPrices_ReturnsMultiplePricesForExactDates() {
        var date = new DateOnly(2026, 3, 2);
        await SeedPrices(
            CreatePrice(_apple, date, 180m),
            CreatePrice(_microsoft, date, 400m)
        );

        var result = await _provider.GetClosingPrices([
            (_apple.Id, date),
            (_microsoft.Id, date),
        ]);

        result.Should().HaveCount(2);
        result[(_apple.Id, date)].Should().Be(180m);
        result[(_microsoft.Id, date)].Should().Be(400m);
    }

    // ── Fallback to nearest prior date ─────────────────────────────────

    [Fact]
    public async Task GetClosingPrices_FallsBackToNearestPriorDate_WithinLookbackWindow() {
        // Price exists 3 days before the requested date (within 7-day window)
        var priceDate = new DateOnly(2026, 3, 10);
        var requestDate = new DateOnly(2026, 3, 13);

        await SeedPrices(CreatePrice(_apple, priceDate, 175m));

        var result = await _provider.GetClosingPrices([(_apple.Id, requestDate)]);

        result.Should().ContainSingle();
        result[(_apple.Id, requestDate)].Should().Be(175m);
    }

    [Fact]
    public async Task GetClosingPrices_PicksClosestPriorDate_WhenMultipleCandidatesExist() {
        var requestDate = new DateOnly(2026, 3, 15);

        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 3, 9), 170m),   // 6 days prior
            CreatePrice(_apple, new DateOnly(2026, 3, 12), 175m),  // 3 days prior (closest)
            CreatePrice(_apple, new DateOnly(2026, 3, 10), 172m)   // 5 days prior
        );

        var result = await _provider.GetClosingPrices([(_apple.Id, requestDate)]);

        result.Should().ContainSingle();
        result[(_apple.Id, requestDate)].Should().Be(175m);
    }

    [Fact]
    public async Task GetClosingPrices_FallsBackToExactly7DaysPrior() {
        // Price at the boundary of the 7-day lookback window
        var priceDate = new DateOnly(2026, 3, 8);
        var requestDate = new DateOnly(2026, 3, 15);

        await SeedPrices(CreatePrice(_apple, priceDate, 165m));

        var result = await _provider.GetClosingPrices([(_apple.Id, requestDate)]);

        result.Should().ContainSingle();
        result[(_apple.Id, requestDate)].Should().Be(165m);
    }

    // ── No price within window ─────────────────────────────────────────

    [Fact]
    public async Task GetClosingPrices_ReturnsEmpty_WhenNoPriceWithinLookbackWindow() {
        // Price exists 8 days before the requested date (outside 7-day window)
        var priceDate = new DateOnly(2026, 3, 7);
        var requestDate = new DateOnly(2026, 3, 15);

        await SeedPrices(CreatePrice(_apple, priceDate, 160m));

        var result = await _provider.GetClosingPrices([(_apple.Id, requestDate)]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetClosingPrices_ReturnsEmpty_WhenNoPricesExistForStock() {
        var requestDate = new DateOnly(2026, 3, 15);

        var result = await _provider.GetClosingPrices([(_apple.Id, requestDate)]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetClosingPrices_IgnoresFuturePrices() {
        var requestDate = new DateOnly(2026, 3, 10);
        var futureDate = new DateOnly(2026, 3, 12);

        await SeedPrices(CreatePrice(_apple, futureDate, 190m));

        var result = await _provider.GetClosingPrices([(_apple.Id, requestDate)]);

        result.Should().BeEmpty();
    }

    // ── Batch handling ─────────────────────────────────────────────────

    [Fact]
    public async Task GetClosingPrices_HandlesBatchWithMixedResults() {
        var date = new DateOnly(2026, 3, 10);
        await SeedPrices(
            CreatePrice(_apple, date, 180m)
            // No price for Microsoft
        );

        var result = await _provider.GetClosingPrices([
            (_apple.Id, date),
            (_microsoft.Id, date),
        ]);

        result.Should().ContainSingle();
        result.Should().ContainKey((_apple.Id, date));
        result.Should().NotContainKey((_microsoft.Id, date));
    }

    [Fact]
    public async Task GetClosingPrices_HandlesMultipleDatesForSameStock() {
        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 1, 10), 170m),
            CreatePrice(_apple, new DateOnly(2026, 2, 10), 180m),
            CreatePrice(_apple, new DateOnly(2026, 3, 10), 190m)
        );

        var result = await _provider.GetClosingPrices([
            (_apple.Id, new DateOnly(2026, 1, 10)),
            (_apple.Id, new DateOnly(2026, 2, 10)),
            (_apple.Id, new DateOnly(2026, 3, 10)),
        ]);

        result.Should().HaveCount(3);
        result[(_apple.Id, new DateOnly(2026, 1, 10))].Should().Be(170m);
        result[(_apple.Id, new DateOnly(2026, 2, 10))].Should().Be(180m);
        result[(_apple.Id, new DateOnly(2026, 3, 10))].Should().Be(190m);
    }

    [Fact]
    public async Task GetClosingPrices_HandlesMultipleStocksWithDifferentDates() {
        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 3, 5), 180m),
            CreatePrice(_microsoft, new DateOnly(2026, 3, 10), 400m),
            CreatePrice(_google, new DateOnly(2026, 3, 15), 150m)
        );

        var result = await _provider.GetClosingPrices([
            (_apple.Id, new DateOnly(2026, 3, 5)),
            (_microsoft.Id, new DateOnly(2026, 3, 10)),
            (_google.Id, new DateOnly(2026, 3, 15)),
        ]);

        result.Should().HaveCount(3);
        result[(_apple.Id, new DateOnly(2026, 3, 5))].Should().Be(180m);
        result[(_microsoft.Id, new DateOnly(2026, 3, 10))].Should().Be(400m);
        result[(_google.Id, new DateOnly(2026, 3, 15))].Should().Be(150m);
    }

    // ── Edge cases ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetClosingPrices_EmptyRequests_ReturnsEmptyDictionary() {
        var result = await _provider.GetClosingPrices([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetClosingPrices_DuplicateRequests_ReturnsSingleEntry() {
        var date = new DateOnly(2026, 3, 10);
        await SeedPrices(CreatePrice(_apple, date, 180m));

        var result = await _provider.GetClosingPrices([
            (_apple.Id, date),
            (_apple.Id, date),
        ]);

        result.Should().ContainSingle();
        result[(_apple.Id, date)].Should().Be(180m);
    }

    [Fact]
    public async Task GetClosingPrices_PreferExactDate_OverEarlierDates() {
        var requestDate = new DateOnly(2026, 3, 10);

        await SeedPrices(
            CreatePrice(_apple, new DateOnly(2026, 3, 8), 170m),
            CreatePrice(_apple, requestDate, 180m)
        );

        var result = await _provider.GetClosingPrices([(_apple.Id, requestDate)]);

        result[(_apple.Id, requestDate)].Should().Be(180m);
    }

    [Fact]
    public async Task GetClosingPrices_SupportsCancellation() {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await _provider.GetClosingPrices(
            [(_apple.Id, new DateOnly(2026, 3, 10))],
            cts.Token
        );

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
