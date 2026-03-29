using Equibles.Cboe.Data;
using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Repositories;
using Equibles.Data;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Cboe;

public class CboePutCallRatioRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly CboePutCallRatioRepository _repository;

    public CboePutCallRatioRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new CboeModuleConfiguration());
        _repository = new CboePutCallRatioRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static CboePutCallRatio CreateRatio(
        CboePutCallRatioType type = CboePutCallRatioType.Total,
        DateOnly? date = null,
        long? callVolume = 2_000_000,
        long? putVolume = 1_500_000,
        long? totalVolume = 3_500_000,
        decimal? putCallRatio = 0.75m) {
        return new CboePutCallRatio {
            Id = Guid.NewGuid(),
            RatioType = type,
            Date = date ?? new DateOnly(2025, 1, 15),
            CallVolume = callVolume,
            PutVolume = putVolume,
            TotalVolume = totalVolume,
            PutCallRatio = putCallRatio,
        };
    }

    // -- GetByType (no date filter) ---------------------------------------

    [Fact]
    public async Task GetByType_ReturnsAllRatiosForType() {
        _dbContext.Set<CboePutCallRatio>().AddRange(
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 2)),
            CreateRatio(CboePutCallRatioType.Equity, new DateOnly(2025, 1, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByType(CboePutCallRatioType.Total).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.RatioType == CboePutCallRatioType.Total);
    }

    [Fact]
    public async Task GetByType_ReturnsEmpty_WhenTypeHasNoData() {
        _dbContext.Set<CboePutCallRatio>().Add(
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByType(CboePutCallRatioType.Vix).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByType_DoesNotReturnOtherTypes() {
        _dbContext.Set<CboePutCallRatio>().AddRange(
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Equity, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Index, new DateOnly(2025, 1, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByType(CboePutCallRatioType.Equity).ToListAsync();

        result.Should().ContainSingle()
            .Which.RatioType.Should().Be(CboePutCallRatioType.Equity);
    }

    // -- GetByType (date range) -------------------------------------------

    [Fact]
    public async Task GetByType_WithDateRange_FiltersCorrectly() {
        _dbContext.Set<CboePutCallRatio>().AddRange(
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 15)),
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 20)),
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 2, 5))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByType(
            CboePutCallRatioType.Total,
            new DateOnly(2025, 1, 10),
            new DateOnly(2025, 1, 25)
        ).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(r => r.Date == new DateOnly(2025, 1, 15));
        result.Should().Contain(r => r.Date == new DateOnly(2025, 1, 20));
    }

    [Fact]
    public async Task GetByType_DateRangeInclusive_IncludesBoundaryDates() {
        _dbContext.Set<CboePutCallRatio>().AddRange(
            CreateRatio(CboePutCallRatioType.Equity, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Equity, new DateOnly(2025, 1, 15)),
            CreateRatio(CboePutCallRatioType.Equity, new DateOnly(2025, 1, 31))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByType(
            CboePutCallRatioType.Equity,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31)
        ).ToListAsync();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetByType_DateRangeExcludesOtherTypes() {
        _dbContext.Set<CboePutCallRatio>().AddRange(
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 15)),
            CreateRatio(CboePutCallRatioType.Equity, new DateOnly(2025, 1, 15))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByType(
            CboePutCallRatioType.Total,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31)
        ).ToListAsync();

        result.Should().ContainSingle()
            .Which.RatioType.Should().Be(CboePutCallRatioType.Total);
    }

    [Fact]
    public async Task GetByType_DateRangeNoMatches_ReturnsEmpty() {
        _dbContext.Set<CboePutCallRatio>().Add(
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 6, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByType(
            CboePutCallRatioType.Total,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31)
        ).ToListAsync();

        result.Should().BeEmpty();
    }

    // -- GetLatestDate ----------------------------------------------------

    [Fact]
    public async Task GetLatestDate_ReturnsMostRecentDateForType() {
        _dbContext.Set<CboePutCallRatio>().AddRange(
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 6, 15)),
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 3, 10))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate(CboePutCallRatioType.Total).FirstOrDefaultAsync();

        result.Should().Be(new DateOnly(2025, 6, 15));
    }

    [Fact]
    public async Task GetLatestDate_NoData_ReturnsDefault() {
        var result = await _repository.GetLatestDate(CboePutCallRatioType.Total).FirstOrDefaultAsync();

        result.Should().Be(default(DateOnly));
    }

    [Fact]
    public async Task GetLatestDate_DoesNotReturnOtherTypeDates() {
        _dbContext.Set<CboePutCallRatio>().AddRange(
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Equity, new DateOnly(2025, 12, 31))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate(CboePutCallRatioType.Total).FirstOrDefaultAsync();

        result.Should().Be(new DateOnly(2025, 1, 1));
    }

    // -- GetLatestPerType -------------------------------------------------

    [Fact]
    public async Task GetLatestPerType_ReturnsMostRecentPerType() {
        _dbContext.Set<CboePutCallRatio>().AddRange(
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 6, 1)),
            CreateRatio(CboePutCallRatioType.Equity, new DateOnly(2025, 3, 1)),
            CreateRatio(CboePutCallRatioType.Equity, new DateOnly(2025, 9, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetLatestPerType().ToList();

        result.Should().HaveCount(2);
        result.Should().Contain(r => r.RatioType == CboePutCallRatioType.Total && r.Date == new DateOnly(2025, 6, 1));
        result.Should().Contain(r => r.RatioType == CboePutCallRatioType.Equity && r.Date == new DateOnly(2025, 9, 1));
    }

    [Fact]
    public async Task GetLatestPerType_NoData_ReturnsEmpty() {
        var result = _repository.GetLatestPerType().ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestPerType_SingleTypeSingleRecord_ReturnsThatRecord() {
        _dbContext.Set<CboePutCallRatio>().Add(
            CreateRatio(CboePutCallRatioType.Vix, new DateOnly(2025, 3, 15), putCallRatio: 1.25m)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetLatestPerType().ToList();

        result.Should().ContainSingle()
            .Which.PutCallRatio.Should().Be(1.25m);
    }

    [Fact]
    public async Task GetLatestPerType_MultipleTypes_ReturnsOnePerType() {
        _dbContext.Set<CboePutCallRatio>().AddRange(
            CreateRatio(CboePutCallRatioType.Total, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Equity, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Index, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Vix, new DateOnly(2025, 1, 1)),
            CreateRatio(CboePutCallRatioType.Etp, new DateOnly(2025, 1, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetLatestPerType().ToList();

        result.Should().HaveCount(5);
        result.Select(r => r.RatioType).Should().BeEquivalentTo(
            new[] {
                CboePutCallRatioType.Total,
                CboePutCallRatioType.Equity,
                CboePutCallRatioType.Index,
                CboePutCallRatioType.Vix,
                CboePutCallRatioType.Etp,
            });
    }

    // -- Field persistence ------------------------------------------------

    [Fact]
    public async Task PutCallRatio_PersistsAllFieldValues() {
        var ratio = CreateRatio(
            CboePutCallRatioType.Total,
            new DateOnly(2025, 7, 15),
            callVolume: 3_000_000,
            putVolume: 2_250_000,
            totalVolume: 5_250_000,
            putCallRatio: 0.75m
        );
        _dbContext.Set<CboePutCallRatio>().Add(ratio);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var result = await _repository.GetByType(CboePutCallRatioType.Total).SingleAsync();

        result.CallVolume.Should().Be(3_000_000);
        result.PutVolume.Should().Be(2_250_000);
        result.TotalVolume.Should().Be(5_250_000);
        result.PutCallRatio.Should().Be(0.75m);
    }

    [Fact]
    public async Task PutCallRatio_PersistsNullableFieldsCorrectly() {
        var ratio = CreateRatio(
            CboePutCallRatioType.Total,
            new DateOnly(2025, 1, 15),
            callVolume: null,
            putVolume: null,
            totalVolume: null,
            putCallRatio: null
        );
        _dbContext.Set<CboePutCallRatio>().Add(ratio);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var result = await _repository.GetByType(CboePutCallRatioType.Total).SingleAsync();

        result.CallVolume.Should().BeNull();
        result.PutVolume.Should().BeNull();
        result.TotalVolume.Should().BeNull();
        result.PutCallRatio.Should().BeNull();
    }
}

public class CboeVixDailyRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly CboeVixDailyRepository _repository;

    public CboeVixDailyRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new CboeModuleConfiguration());
        _repository = new CboeVixDailyRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static CboeVixDaily CreateVix(
        DateOnly? date = null,
        decimal open = 15.50m,
        decimal high = 16.20m,
        decimal low = 14.80m,
        decimal close = 15.90m) {
        return new CboeVixDaily {
            Id = Guid.NewGuid(),
            Date = date ?? new DateOnly(2025, 1, 15),
            Open = open,
            High = high,
            Low = low,
            Close = close,
        };
    }

    // -- GetByDateRange ---------------------------------------------------

    [Fact]
    public async Task GetByDateRange_ReturnsRecordsWithinRange() {
        _dbContext.Set<CboeVixDaily>().AddRange(
            CreateVix(new DateOnly(2025, 1, 1)),
            CreateVix(new DateOnly(2025, 1, 15)),
            CreateVix(new DateOnly(2025, 1, 20)),
            CreateVix(new DateOnly(2025, 2, 5))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByDateRange(
            new DateOnly(2025, 1, 10),
            new DateOnly(2025, 1, 25)
        ).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(v => v.Date == new DateOnly(2025, 1, 15));
        result.Should().Contain(v => v.Date == new DateOnly(2025, 1, 20));
    }

    [Fact]
    public async Task GetByDateRange_IncludesBoundaryDates() {
        _dbContext.Set<CboeVixDaily>().AddRange(
            CreateVix(new DateOnly(2025, 1, 1)),
            CreateVix(new DateOnly(2025, 1, 15)),
            CreateVix(new DateOnly(2025, 1, 31))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByDateRange(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31)
        ).ToListAsync();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetByDateRange_NoMatches_ReturnsEmpty() {
        _dbContext.Set<CboeVixDaily>().Add(
            CreateVix(new DateOnly(2025, 6, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByDateRange(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31)
        ).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByDateRange_ReturnsEmpty_WhenNoDataExists() {
        var result = await _repository.GetByDateRange(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31)
        ).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByDateRange_SingleDay_ReturnsMatchingRecord() {
        var date = new DateOnly(2025, 3, 15);
        _dbContext.Set<CboeVixDaily>().Add(CreateVix(date, close: 22.50m));
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByDateRange(date, date).ToListAsync();

        result.Should().ContainSingle()
            .Which.Close.Should().Be(22.50m);
    }

    // -- GetLatestDate ----------------------------------------------------

    [Fact]
    public async Task GetLatestDate_ReturnsMostRecentDate() {
        _dbContext.Set<CboeVixDaily>().AddRange(
            CreateVix(new DateOnly(2025, 1, 1)),
            CreateVix(new DateOnly(2025, 6, 15)),
            CreateVix(new DateOnly(2025, 3, 10))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate().FirstOrDefaultAsync();

        result.Should().Be(new DateOnly(2025, 6, 15));
    }

    [Fact]
    public async Task GetLatestDate_NoData_ReturnsDefault() {
        var result = await _repository.GetLatestDate().FirstOrDefaultAsync();

        result.Should().Be(default(DateOnly));
    }

    [Fact]
    public async Task GetLatestDate_SingleRecord_ReturnsThatDate() {
        _dbContext.Set<CboeVixDaily>().Add(
            CreateVix(new DateOnly(2025, 3, 15))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(new DateOnly(2025, 3, 15));
    }

    // -- Field persistence ------------------------------------------------

    [Fact]
    public async Task VixDaily_PersistsAllFieldValues() {
        var vix = CreateVix(
            new DateOnly(2025, 7, 15),
            open: 18.25m,
            high: 20.10m,
            low: 17.80m,
            close: 19.45m
        );
        _dbContext.Set<CboeVixDaily>().Add(vix);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var result = await _repository.GetByDateRange(
            new DateOnly(2025, 7, 15),
            new DateOnly(2025, 7, 15)
        ).SingleAsync();

        result.Open.Should().Be(18.25m);
        result.High.Should().Be(20.10m);
        result.Low.Should().Be(17.80m);
        result.Close.Should().Be(19.45m);
    }
}
