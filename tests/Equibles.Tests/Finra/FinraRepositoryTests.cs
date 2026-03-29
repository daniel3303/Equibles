using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Finra;

public class DailyShortVolumeRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly DailyShortVolumeRepository _repository;

    public DailyShortVolumeRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration()
        );
        _repository = new DailyShortVolumeRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc.") {
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = ticker, Name = name };
        _dbContext.Set<CommonStock>().Add(stock);
        return stock;
    }

    private DailyShortVolume CreateVolume(
        CommonStock stock,
        DateOnly date,
        long shortVolume = 1_000_000,
        long shortExemptVolume = 5_000,
        long totalVolume = 5_000_000,
        string market = "TRF") {
        return new DailyShortVolume {
            CommonStockId = stock.Id,
            Date = date,
            ShortVolume = shortVolume,
            ShortExemptVolume = shortExemptVolume,
            TotalVolume = totalVolume,
            Market = market,
        };
    }

    // -- GetHistoryByStock ------------------------------------------------

    [Fact]
    public async Task GetHistoryByStock_ReturnsAllVolumesForStock() {
        var stock = CreateStock();
        _dbContext.Set<DailyShortVolume>().AddRange(
            CreateVolume(stock, new DateOnly(2025, 1, 1)),
            CreateVolume(stock, new DateOnly(2025, 1, 2)),
            CreateVolume(stock, new DateOnly(2025, 1, 3))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetHistoryByStock(stock).ToListAsync();

        result.Should().HaveCount(3);
        result.Should().OnlyContain(v => v.CommonStockId == stock.Id);
    }

    [Fact]
    public async Task GetHistoryByStock_ReturnsEmpty_WhenStockHasNoData() {
        var stockWithData = CreateStock("AAPL", "Apple");
        var stockWithout = CreateStock("GOOG", "Alphabet");
        _dbContext.Set<DailyShortVolume>().Add(
            CreateVolume(stockWithData, new DateOnly(2025, 1, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetHistoryByStock(stockWithout).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryByStock_DoesNotReturnVolumesFromOtherStocks() {
        var apple = CreateStock("AAPL", "Apple");
        var msft = CreateStock("MSFT", "Microsoft");
        _dbContext.Set<DailyShortVolume>().AddRange(
            CreateVolume(apple, new DateOnly(2025, 1, 1)),
            CreateVolume(msft, new DateOnly(2025, 1, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetHistoryByStock(apple).ToListAsync();

        result.Should().ContainSingle()
            .Which.CommonStockId.Should().Be(apple.Id);
    }

    // -- GetByStock (date filter) -----------------------------------------

    [Fact]
    public async Task GetByStock_FindsRecordForSpecificDate() {
        var stock = CreateStock();
        var targetDate = new DateOnly(2025, 3, 15);
        _dbContext.Set<DailyShortVolume>().AddRange(
            CreateVolume(stock, new DateOnly(2025, 3, 14)),
            CreateVolume(stock, targetDate, shortVolume: 2_000_000),
            CreateVolume(stock, new DateOnly(2025, 3, 16))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByStock(stock, targetDate).ToListAsync();

        result.Should().ContainSingle()
            .Which.ShortVolume.Should().Be(2_000_000);
    }

    [Fact]
    public async Task GetByStock_ReturnsEmpty_WhenNoRecordForDate() {
        var stock = CreateStock();
        _dbContext.Set<DailyShortVolume>().Add(
            CreateVolume(stock, new DateOnly(2025, 1, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByStock(stock, new DateOnly(2025, 12, 31)).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByStock_ReturnsEmpty_WhenDateExistsForDifferentStock() {
        var apple = CreateStock("AAPL", "Apple");
        var msft = CreateStock("MSFT", "Microsoft");
        var date = new DateOnly(2025, 5, 1);
        _dbContext.Set<DailyShortVolume>().Add(CreateVolume(apple, date));
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByStock(msft, date).ToListAsync();

        result.Should().BeEmpty();
    }

    // -- GetLatestDate ----------------------------------------------------

    [Fact]
    public async Task GetLatestDate_ReturnsMostRecentDate() {
        var stock = CreateStock();
        _dbContext.Set<DailyShortVolume>().AddRange(
            CreateVolume(stock, new DateOnly(2025, 1, 1)),
            CreateVolume(stock, new DateOnly(2025, 6, 15)),
            CreateVolume(stock, new DateOnly(2025, 3, 10))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(new DateOnly(2025, 6, 15));
    }

    [Fact]
    public async Task GetLatestDate_ReturnsEmpty_WhenNoRecordsExist() {
        var result = await _repository.GetLatestDate().ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestDate_ReturnsSingleDate_WhenMultipleStocksShareLatestDate() {
        var apple = CreateStock("AAPL", "Apple");
        var msft = CreateStock("MSFT", "Microsoft");
        var latestDate = new DateOnly(2025, 6, 15);
        _dbContext.Set<DailyShortVolume>().AddRange(
            CreateVolume(apple, latestDate),
            CreateVolume(msft, latestDate),
            CreateVolume(apple, new DateOnly(2025, 1, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(latestDate);
    }

    // -- GetByDate --------------------------------------------------------

    [Fact]
    public async Task GetByDate_ReturnsAllRecordsForGivenDate() {
        var apple = CreateStock("AAPL", "Apple");
        var msft = CreateStock("MSFT", "Microsoft");
        var date = new DateOnly(2025, 4, 1);
        _dbContext.Set<DailyShortVolume>().AddRange(
            CreateVolume(apple, date),
            CreateVolume(msft, date),
            CreateVolume(apple, new DateOnly(2025, 4, 2))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByDate(date).ToListAsync();

        result.Should().HaveCount(2);
        result.Select(v => v.CommonStockId).Should().Contain(new[] { apple.Id, msft.Id });
    }

    [Fact]
    public async Task GetByDate_ReturnsEmpty_WhenNoRecordsForDate() {
        var stock = CreateStock();
        _dbContext.Set<DailyShortVolume>().Add(
            CreateVolume(stock, new DateOnly(2025, 1, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByDate(new DateOnly(2099, 1, 1)).ToListAsync();

        result.Should().BeEmpty();
    }
}

public class ShortInterestRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly ShortInterestRepository _repository;

    public ShortInterestRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration()
        );
        _repository = new ShortInterestRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc.") {
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = ticker, Name = name };
        _dbContext.Set<CommonStock>().Add(stock);
        return stock;
    }

    private ShortInterest CreateInterest(
        CommonStock stock,
        DateOnly settlementDate,
        long currentShortPosition = 10_000_000,
        long previousShortPosition = 9_500_000,
        long changeInShortPosition = 500_000,
        long? averageDailyVolume = 3_000_000,
        decimal? daysToCover = 3.3m) {
        return new ShortInterest {
            CommonStockId = stock.Id,
            SettlementDate = settlementDate,
            CurrentShortPosition = currentShortPosition,
            PreviousShortPosition = previousShortPosition,
            ChangeInShortPosition = changeInShortPosition,
            AverageDailyVolume = averageDailyVolume,
            DaysToCover = daysToCover,
        };
    }

    // -- GetHistoryByStock ------------------------------------------------

    [Fact]
    public async Task GetHistoryByStock_ReturnsAllInterestRecordsForStock() {
        var stock = CreateStock();
        _dbContext.Set<ShortInterest>().AddRange(
            CreateInterest(stock, new DateOnly(2025, 1, 15)),
            CreateInterest(stock, new DateOnly(2025, 2, 15)),
            CreateInterest(stock, new DateOnly(2025, 3, 15))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetHistoryByStock(stock).ToListAsync();

        result.Should().HaveCount(3);
        result.Should().OnlyContain(s => s.CommonStockId == stock.Id);
    }

    [Fact]
    public async Task GetHistoryByStock_ReturnsEmpty_WhenStockHasNoData() {
        var stockWithData = CreateStock("AAPL", "Apple");
        var stockWithout = CreateStock("GOOG", "Alphabet");
        _dbContext.Set<ShortInterest>().Add(
            CreateInterest(stockWithData, new DateOnly(2025, 1, 15))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetHistoryByStock(stockWithout).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryByStock_DoesNotReturnRecordsFromOtherStocks() {
        var apple = CreateStock("AAPL", "Apple");
        var msft = CreateStock("MSFT", "Microsoft");
        _dbContext.Set<ShortInterest>().AddRange(
            CreateInterest(apple, new DateOnly(2025, 1, 15)),
            CreateInterest(msft, new DateOnly(2025, 1, 15))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetHistoryByStock(apple).ToListAsync();

        result.Should().ContainSingle()
            .Which.CommonStockId.Should().Be(apple.Id);
    }

    // -- GetByStock (settlement date filter) ------------------------------

    [Fact]
    public async Task GetByStock_FindsRecordForSpecificSettlementDate() {
        var stock = CreateStock();
        var targetDate = new DateOnly(2025, 3, 15);
        _dbContext.Set<ShortInterest>().AddRange(
            CreateInterest(stock, new DateOnly(2025, 2, 15)),
            CreateInterest(stock, targetDate, currentShortPosition: 15_000_000),
            CreateInterest(stock, new DateOnly(2025, 4, 15))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByStock(stock, targetDate).ToListAsync();

        result.Should().ContainSingle()
            .Which.CurrentShortPosition.Should().Be(15_000_000);
    }

    [Fact]
    public async Task GetByStock_ReturnsEmpty_WhenNoRecordForSettlementDate() {
        var stock = CreateStock();
        _dbContext.Set<ShortInterest>().Add(
            CreateInterest(stock, new DateOnly(2025, 1, 15))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByStock(stock, new DateOnly(2025, 12, 31)).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByStock_ReturnsEmpty_WhenDateExistsForDifferentStock() {
        var apple = CreateStock("AAPL", "Apple");
        var msft = CreateStock("MSFT", "Microsoft");
        var date = new DateOnly(2025, 5, 15);
        _dbContext.Set<ShortInterest>().Add(CreateInterest(apple, date));
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByStock(msft, date).ToListAsync();

        result.Should().BeEmpty();
    }

    // -- GetLatestSettlementDate ------------------------------------------

    [Fact]
    public async Task GetLatestSettlementDate_ReturnsMostRecentDate() {
        var stock = CreateStock();
        _dbContext.Set<ShortInterest>().AddRange(
            CreateInterest(stock, new DateOnly(2025, 1, 15)),
            CreateInterest(stock, new DateOnly(2025, 6, 15)),
            CreateInterest(stock, new DateOnly(2025, 3, 15))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestSettlementDate().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(new DateOnly(2025, 6, 15));
    }

    [Fact]
    public async Task GetLatestSettlementDate_ReturnsEmpty_WhenNoRecordsExist() {
        var result = await _repository.GetLatestSettlementDate().ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestSettlementDate_ReturnsSingleDate_WhenMultipleStocksShareIt() {
        var apple = CreateStock("AAPL", "Apple");
        var msft = CreateStock("MSFT", "Microsoft");
        var latestDate = new DateOnly(2025, 6, 15);
        _dbContext.Set<ShortInterest>().AddRange(
            CreateInterest(apple, latestDate),
            CreateInterest(msft, latestDate),
            CreateInterest(apple, new DateOnly(2025, 1, 15))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestSettlementDate().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(latestDate);
    }

    // -- GetBySettlementDate ----------------------------------------------

    [Fact]
    public async Task GetBySettlementDate_ReturnsAllRecordsForGivenDate() {
        var apple = CreateStock("AAPL", "Apple");
        var msft = CreateStock("MSFT", "Microsoft");
        var date = new DateOnly(2025, 4, 15);
        _dbContext.Set<ShortInterest>().AddRange(
            CreateInterest(apple, date),
            CreateInterest(msft, date),
            CreateInterest(apple, new DateOnly(2025, 5, 15))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetBySettlementDate(date).ToListAsync();

        result.Should().HaveCount(2);
        result.Select(s => s.CommonStockId).Should().Contain(new[] { apple.Id, msft.Id });
    }

    [Fact]
    public async Task GetBySettlementDate_ReturnsEmpty_WhenNoRecordsForDate() {
        var stock = CreateStock();
        _dbContext.Set<ShortInterest>().Add(
            CreateInterest(stock, new DateOnly(2025, 1, 15))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetBySettlementDate(new DateOnly(2099, 1, 1)).ToListAsync();

        result.Should().BeEmpty();
    }

    // -- Nullable fields --------------------------------------------------

    [Fact]
    public async Task ShortInterest_PersistsNullableFieldsCorrectly() {
        var stock = CreateStock();
        _dbContext.Set<ShortInterest>().Add(
            CreateInterest(stock, new DateOnly(2025, 1, 15), averageDailyVolume: null, daysToCover: null)
        );
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var result = await _repository.GetHistoryByStock(stock).SingleAsync();

        result.AverageDailyVolume.Should().BeNull();
        result.DaysToCover.Should().BeNull();
    }

    [Fact]
    public async Task ShortInterest_PersistsAllFieldValues() {
        var stock = CreateStock();
        var interest = CreateInterest(
            stock,
            new DateOnly(2025, 7, 15),
            currentShortPosition: 20_000_000,
            previousShortPosition: 18_000_000,
            changeInShortPosition: 2_000_000,
            averageDailyVolume: 5_000_000,
            daysToCover: 4.0m
        );
        _dbContext.Set<ShortInterest>().Add(interest);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var result = await _repository.GetHistoryByStock(stock).SingleAsync();

        result.CurrentShortPosition.Should().Be(20_000_000);
        result.PreviousShortPosition.Should().Be(18_000_000);
        result.ChangeInShortPosition.Should().Be(2_000_000);
        result.AverageDailyVolume.Should().Be(5_000_000);
        result.DaysToCover.Should().Be(4.0m);
    }
}
