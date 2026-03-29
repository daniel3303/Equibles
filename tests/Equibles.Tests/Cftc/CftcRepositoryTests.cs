using Equibles.Cftc.Data;
using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Repositories;
using Equibles.Data;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Cftc;

public class CftcContractRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly CftcContractRepository _repository;

    public CftcContractRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new CftcModuleConfiguration());
        _repository = new CftcContractRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static CftcContract CreateContract(
        string marketCode = "WTI",
        string marketName = "Crude Oil, Light Sweet",
        CftcContractCategory category = CftcContractCategory.Energy) {
        return new CftcContract {
            Id = Guid.NewGuid(),
            MarketCode = marketCode,
            MarketName = marketName,
            Category = category,
        };
    }

    // -- GetByMarketCode --------------------------------------------------

    [Fact]
    public async Task GetByMarketCode_ExistingCode_ReturnsContract() {
        _dbContext.Set<CftcContract>().Add(CreateContract("WTI", "Crude Oil, Light Sweet"));
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByMarketCode("WTI").FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result!.MarketCode.Should().Be("WTI");
        result.MarketName.Should().Be("Crude Oil, Light Sweet");
    }

    [Fact]
    public async Task GetByMarketCode_NonExistentCode_ReturnsNull() {
        var result = await _repository.GetByMarketCode("NONEXISTENT").FirstOrDefaultAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByMarketCode_DoesNotReturnOtherContracts() {
        _dbContext.Set<CftcContract>().AddRange(
            CreateContract("WTI", "Crude Oil"),
            CreateContract("NG", "Natural Gas")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByMarketCode("WTI").ToList();

        result.Should().ContainSingle()
            .Which.MarketCode.Should().Be("WTI");
    }

    // -- GetByCategory ----------------------------------------------------

    [Fact]
    public async Task GetByCategory_ReturnsContractsInCategory() {
        _dbContext.Set<CftcContract>().AddRange(
            CreateContract("WTI", "Crude Oil", CftcContractCategory.Energy),
            CreateContract("NG", "Natural Gas", CftcContractCategory.Energy),
            CreateContract("GC", "Gold", CftcContractCategory.Metals)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByCategory(CftcContractCategory.Energy).ToList();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(c => c.Category.Should().Be(CftcContractCategory.Energy));
    }

    [Fact]
    public async Task GetByCategory_EmptyCategory_ReturnsEmpty() {
        _dbContext.Set<CftcContract>().Add(
            CreateContract("WTI", "Crude Oil", CftcContractCategory.Energy)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByCategory(CftcContractCategory.Agriculture).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCategory_AllCategories_FiltersCorrectly() {
        _dbContext.Set<CftcContract>().AddRange(
            CreateContract("WTI", "Crude Oil", CftcContractCategory.Energy),
            CreateContract("GC", "Gold", CftcContractCategory.Metals),
            CreateContract("ZC", "Corn", CftcContractCategory.Agriculture),
            CreateContract("ES", "E-mini S&P 500", CftcContractCategory.EquityIndices)
        );
        await _dbContext.SaveChangesAsync();

        _repository.GetByCategory(CftcContractCategory.Energy).ToList().Should().ContainSingle();
        _repository.GetByCategory(CftcContractCategory.Metals).ToList().Should().ContainSingle();
        _repository.GetByCategory(CftcContractCategory.Agriculture).ToList().Should().ContainSingle();
        _repository.GetByCategory(CftcContractCategory.EquityIndices).ToList().Should().ContainSingle();
        _repository.GetByCategory(CftcContractCategory.Currencies).ToList().Should().BeEmpty();
    }

    // -- Search -----------------------------------------------------------

    [Fact]
    public async Task Search_ByMarketCode_FindsMatch() {
        _dbContext.Set<CftcContract>().AddRange(
            CreateContract("WTI", "Crude Oil, Light Sweet"),
            CreateContract("NG", "Natural Gas")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("WTI").ToList();

        result.Should().ContainSingle()
            .Which.MarketCode.Should().Be("WTI");
    }

    [Fact]
    public async Task Search_ByMarketName_FindsMatch() {
        _dbContext.Set<CftcContract>().AddRange(
            CreateContract("WTI", "Crude Oil, Light Sweet"),
            CreateContract("NG", "Natural Gas")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("Natural Gas").ToList();

        result.Should().ContainSingle()
            .Which.MarketCode.Should().Be("NG");
    }

    [Fact]
    public async Task Search_CaseInsensitive_FindsMatch() {
        _dbContext.Set<CftcContract>().Add(
            CreateContract("WTI", "Crude Oil, Light Sweet")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("crude oil").ToList();

        result.Should().ContainSingle()
            .Which.MarketCode.Should().Be("WTI");
    }

    [Fact]
    public async Task Search_PartialMatch_FindsMatch() {
        _dbContext.Set<CftcContract>().Add(
            CreateContract("WTI", "Crude Oil, Light Sweet")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("Crude").ToList();

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty() {
        _dbContext.Set<CftcContract>().Add(
            CreateContract("WTI", "Crude Oil, Light Sweet")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("NONEXISTENT").ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_MatchesMultipleContracts_ReturnsAll() {
        _dbContext.Set<CftcContract>().AddRange(
            CreateContract("WTI", "Crude Oil, Light Sweet"),
            CreateContract("BRN", "Crude Oil, Brent")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("Crude").ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_MatchesBothCodeAndName_ReturnsAll() {
        _dbContext.Set<CftcContract>().AddRange(
            CreateContract("GOLD", "Gold Futures"),
            CreateContract("GC", "GOLD 100 Troy Ounces")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("GOLD").ToList();

        result.Should().HaveCount(2);
    }
}

public class CftcPositionReportRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly CftcPositionReportRepository _repository;

    public CftcPositionReportRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new CftcModuleConfiguration());
        _repository = new CftcPositionReportRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private CftcContract CreateContract(
        string marketCode = "WTI",
        string marketName = "Crude Oil, Light Sweet",
        CftcContractCategory category = CftcContractCategory.Energy) {
        var contract = new CftcContract {
            Id = Guid.NewGuid(),
            MarketCode = marketCode,
            MarketName = marketName,
            Category = category,
        };
        _dbContext.Set<CftcContract>().Add(contract);
        return contract;
    }

    private static CftcPositionReport CreateReport(
        Guid contractId,
        DateOnly reportDate,
        long openInterest = 500_000,
        long nonCommLong = 200_000,
        long nonCommShort = 150_000) {
        return new CftcPositionReport {
            Id = Guid.NewGuid(),
            CftcContractId = contractId,
            ReportDate = reportDate,
            OpenInterest = openInterest,
            NonCommLong = nonCommLong,
            NonCommShort = nonCommShort,
            NonCommSpreads = 50_000,
            CommLong = 180_000,
            CommShort = 200_000,
            TotalRptLong = 430_000,
            TotalRptShort = 400_000,
            NonRptLong = 70_000,
            NonRptShort = 100_000,
        };
    }

    // -- GetByContract (all reports) --------------------------------------

    [Fact]
    public async Task GetByContract_ReturnsAllReportsForContract() {
        var contract = CreateContract();
        _dbContext.Set<CftcPositionReport>().AddRange(
            CreateReport(contract.Id, new DateOnly(2025, 1, 7)),
            CreateReport(contract.Id, new DateOnly(2025, 1, 14)),
            CreateReport(contract.Id, new DateOnly(2025, 1, 21))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByContract(contract).ToListAsync();

        result.Should().HaveCount(3);
        result.Should().OnlyContain(r => r.CftcContractId == contract.Id);
    }

    [Fact]
    public async Task GetByContract_ReturnsEmpty_WhenContractHasNoReports() {
        var contractWithData = CreateContract("WTI", "Crude Oil");
        var contractWithout = CreateContract("NG", "Natural Gas");
        _dbContext.Set<CftcPositionReport>().Add(
            CreateReport(contractWithData.Id, new DateOnly(2025, 1, 7))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByContract(contractWithout).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByContract_DoesNotReturnReportsFromOtherContracts() {
        var wti = CreateContract("WTI", "Crude Oil");
        var ng = CreateContract("NG", "Natural Gas");
        _dbContext.Set<CftcPositionReport>().AddRange(
            CreateReport(wti.Id, new DateOnly(2025, 1, 7)),
            CreateReport(ng.Id, new DateOnly(2025, 1, 7))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByContract(wti).ToListAsync();

        result.Should().ContainSingle()
            .Which.CftcContractId.Should().Be(wti.Id);
    }

    // -- GetByContract (date range) ---------------------------------------

    [Fact]
    public async Task GetByContract_WithDateRange_FiltersCorrectly() {
        var contract = CreateContract();
        _dbContext.Set<CftcPositionReport>().AddRange(
            CreateReport(contract.Id, new DateOnly(2025, 1, 7)),
            CreateReport(contract.Id, new DateOnly(2025, 1, 14)),
            CreateReport(contract.Id, new DateOnly(2025, 1, 21)),
            CreateReport(contract.Id, new DateOnly(2025, 2, 4))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByContract(
            contract,
            new DateOnly(2025, 1, 10),
            new DateOnly(2025, 1, 25)
        ).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(r => r.ReportDate == new DateOnly(2025, 1, 14));
        result.Should().Contain(r => r.ReportDate == new DateOnly(2025, 1, 21));
    }

    [Fact]
    public async Task GetByContract_DateRangeInclusive_IncludesBoundaryDates() {
        var contract = CreateContract();
        _dbContext.Set<CftcPositionReport>().AddRange(
            CreateReport(contract.Id, new DateOnly(2025, 1, 7)),
            CreateReport(contract.Id, new DateOnly(2025, 1, 14)),
            CreateReport(contract.Id, new DateOnly(2025, 1, 21))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByContract(
            contract,
            new DateOnly(2025, 1, 7),
            new DateOnly(2025, 1, 21)
        ).ToListAsync();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetByContract_DateRangeExcludesOtherContracts() {
        var wti = CreateContract("WTI", "Crude Oil");
        var ng = CreateContract("NG", "Natural Gas");
        _dbContext.Set<CftcPositionReport>().AddRange(
            CreateReport(wti.Id, new DateOnly(2025, 1, 14)),
            CreateReport(ng.Id, new DateOnly(2025, 1, 14))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByContract(
            wti,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31)
        ).ToListAsync();

        result.Should().ContainSingle()
            .Which.CftcContractId.Should().Be(wti.Id);
    }

    [Fact]
    public async Task GetByContract_DateRangeNoMatches_ReturnsEmpty() {
        var contract = CreateContract();
        _dbContext.Set<CftcPositionReport>().Add(
            CreateReport(contract.Id, new DateOnly(2025, 6, 1))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByContract(
            contract,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31)
        ).ToListAsync();

        result.Should().BeEmpty();
    }

    // -- GetLatestDate ----------------------------------------------------

    [Fact]
    public async Task GetLatestDate_ReturnsMostRecentDateForContract() {
        var contract = CreateContract();
        _dbContext.Set<CftcPositionReport>().AddRange(
            CreateReport(contract.Id, new DateOnly(2025, 1, 7)),
            CreateReport(contract.Id, new DateOnly(2025, 6, 17)),
            CreateReport(contract.Id, new DateOnly(2025, 3, 11))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate(contract).FirstOrDefaultAsync();

        result.Should().Be(new DateOnly(2025, 6, 17));
    }

    [Fact]
    public async Task GetLatestDate_NoReports_ReturnsDefault() {
        var contract = CreateContract();
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate(contract).FirstOrDefaultAsync();

        result.Should().Be(default(DateOnly));
    }

    [Fact]
    public async Task GetLatestDate_DoesNotReturnOtherContractDates() {
        var wti = CreateContract("WTI", "Crude Oil");
        var ng = CreateContract("NG", "Natural Gas");
        _dbContext.Set<CftcPositionReport>().AddRange(
            CreateReport(wti.Id, new DateOnly(2025, 1, 7)),
            CreateReport(ng.Id, new DateOnly(2025, 12, 30))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate(wti).FirstOrDefaultAsync();

        result.Should().Be(new DateOnly(2025, 1, 7));
    }

    // -- GetLatestPerContract ---------------------------------------------

    [Fact]
    public async Task GetLatestPerContract_ReturnsMostRecentPerContract() {
        var wti = CreateContract("WTI", "Crude Oil");
        var ng = CreateContract("NG", "Natural Gas");
        _dbContext.Set<CftcPositionReport>().AddRange(
            CreateReport(wti.Id, new DateOnly(2025, 1, 7)),
            CreateReport(wti.Id, new DateOnly(2025, 6, 3)),
            CreateReport(ng.Id, new DateOnly(2025, 3, 4)),
            CreateReport(ng.Id, new DateOnly(2025, 9, 2))
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetLatestPerContract().ToList();

        result.Should().HaveCount(2);
        result.Should().Contain(r => r.CftcContractId == wti.Id && r.ReportDate == new DateOnly(2025, 6, 3));
        result.Should().Contain(r => r.CftcContractId == ng.Id && r.ReportDate == new DateOnly(2025, 9, 2));
    }

    [Fact]
    public async Task GetLatestPerContract_NoReports_ReturnsEmpty() {
        var result = _repository.GetLatestPerContract().ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestPerContract_SingleContractSingleReport_ReturnsThatReport() {
        var contract = CreateContract();
        _dbContext.Set<CftcPositionReport>().Add(
            CreateReport(contract.Id, new DateOnly(2025, 3, 18), openInterest: 750_000)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetLatestPerContract().ToList();

        result.Should().ContainSingle()
            .Which.OpenInterest.Should().Be(750_000);
    }

    // -- GetGlobalLatestDate ----------------------------------------------

    [Fact]
    public async Task GetGlobalLatestDate_ReturnsMostRecentDateAcrossAllContracts() {
        var wti = CreateContract("WTI", "Crude Oil");
        var ng = CreateContract("NG", "Natural Gas");
        _dbContext.Set<CftcPositionReport>().AddRange(
            CreateReport(wti.Id, new DateOnly(2025, 1, 7)),
            CreateReport(ng.Id, new DateOnly(2025, 6, 17)),
            CreateReport(wti.Id, new DateOnly(2025, 3, 11))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetGlobalLatestDate().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(new DateOnly(2025, 6, 17));
    }

    [Fact]
    public async Task GetGlobalLatestDate_ReturnsEmpty_WhenNoReportsExist() {
        var result = await _repository.GetGlobalLatestDate().ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGlobalLatestDate_ReturnsSingleDate_WhenMultipleContractsShareLatestDate() {
        var wti = CreateContract("WTI", "Crude Oil");
        var ng = CreateContract("NG", "Natural Gas");
        var latestDate = new DateOnly(2025, 6, 17);
        _dbContext.Set<CftcPositionReport>().AddRange(
            CreateReport(wti.Id, latestDate),
            CreateReport(ng.Id, latestDate),
            CreateReport(wti.Id, new DateOnly(2025, 1, 7))
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetGlobalLatestDate().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(latestDate);
    }

    // -- Field persistence ------------------------------------------------

    [Fact]
    public async Task PositionReport_PersistsAllFieldValues() {
        var contract = CreateContract();
        var report = new CftcPositionReport {
            Id = Guid.NewGuid(),
            CftcContractId = contract.Id,
            ReportDate = new DateOnly(2025, 7, 15),
            OpenInterest = 500_000,
            NonCommLong = 200_000,
            NonCommShort = 150_000,
            NonCommSpreads = 50_000,
            CommLong = 180_000,
            CommShort = 200_000,
            TotalRptLong = 430_000,
            TotalRptShort = 400_000,
            NonRptLong = 70_000,
            NonRptShort = 100_000,
            ChangeOpenInterest = 10_000,
            ChangeNonCommLong = 5_000,
            ChangeNonCommShort = -3_000,
            ChangeCommLong = 2_000,
            ChangeCommShort = -1_000,
            PctNonCommLong = 40.0m,
            PctNonCommShort = 30.0m,
            PctCommLong = 36.0m,
            PctCommShort = 40.0m,
            TradersTotal = 250,
            TradersNonCommLong = 80,
            TradersNonCommShort = 60,
            TradersCommLong = 50,
            TradersCommShort = 45,
        };
        _dbContext.Set<CftcPositionReport>().Add(report);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var result = await _repository.GetByContract(contract).SingleAsync();

        result.OpenInterest.Should().Be(500_000);
        result.NonCommLong.Should().Be(200_000);
        result.NonCommShort.Should().Be(150_000);
        result.NonCommSpreads.Should().Be(50_000);
        result.CommLong.Should().Be(180_000);
        result.CommShort.Should().Be(200_000);
        result.TotalRptLong.Should().Be(430_000);
        result.TotalRptShort.Should().Be(400_000);
        result.NonRptLong.Should().Be(70_000);
        result.NonRptShort.Should().Be(100_000);
        result.ChangeOpenInterest.Should().Be(10_000);
        result.ChangeNonCommLong.Should().Be(5_000);
        result.ChangeNonCommShort.Should().Be(-3_000);
        result.ChangeCommLong.Should().Be(2_000);
        result.ChangeCommShort.Should().Be(-1_000);
        result.PctNonCommLong.Should().Be(40.0m);
        result.PctNonCommShort.Should().Be(30.0m);
        result.PctCommLong.Should().Be(36.0m);
        result.PctCommShort.Should().Be(40.0m);
        result.TradersTotal.Should().Be(250);
        result.TradersNonCommLong.Should().Be(80);
        result.TradersNonCommShort.Should().Be(60);
        result.TradersCommLong.Should().Be(50);
        result.TradersCommShort.Should().Be(45);
    }

    [Fact]
    public async Task PositionReport_PersistsNullableFieldsCorrectly() {
        var contract = CreateContract();
        var report = CreateReport(contract.Id, new DateOnly(2025, 1, 7));
        // Nullable fields default to null in CreateReport helper
        _dbContext.Set<CftcPositionReport>().Add(report);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var result = await _repository.GetByContract(contract).SingleAsync();

        result.ChangeOpenInterest.Should().BeNull();
        result.ChangeNonCommLong.Should().BeNull();
        result.ChangeNonCommShort.Should().BeNull();
        result.ChangeCommLong.Should().BeNull();
        result.ChangeCommShort.Should().BeNull();
        result.PctNonCommLong.Should().BeNull();
        result.PctNonCommShort.Should().BeNull();
        result.PctCommLong.Should().BeNull();
        result.PctCommShort.Should().BeNull();
        result.TradersTotal.Should().BeNull();
        result.TradersNonCommLong.Should().BeNull();
        result.TradersNonCommShort.Should().BeNull();
        result.TradersCommLong.Should().BeNull();
        result.TradersCommShort.Should().BeNull();
    }
}
