using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Sec;

public class NportFilingRepositoryTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly NportFilingRepository _repository;

    public NportFilingRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        _repository = new NportFilingRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static CommonStock CreateStock(string ticker = "VOO", string cik = "0000036405")
    {
        return new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
            Cik = cik,
        };
    }

    private static NportFiling CreateFiling(
        Guid commonStockId,
        string accessionNumber = "0000036405-24-000002",
        DateOnly? filingDate = null,
        string seriesName = "Vanguard 500 Index Fund"
    )
    {
        return new NportFiling
        {
            Id = Guid.NewGuid(),
            CommonStockId = commonStockId,
            AccessionNumber = accessionNumber,
            FilingDate = filingDate ?? new DateOnly(2025, 1, 15),
            IsAmendment = false,
            RegistrantName = "VANGUARD INDEX FUNDS",
            SeriesName = seriesName,
            SeriesId = "S000002277",
            SeriesLei = "5493007GHODQRGNTGS28",
            ReportPeriodDate = new DateOnly(2024, 12, 31),
            ReportPeriodEnd = new DateOnly(2025, 12, 31),
            TotalAssets = 1_200_000_000m,
            TotalLiabilities = 50_000_000m,
            NetAssets = 1_150_000_000m,
            IsFinalFiling = false,
        };
    }

    [Fact]
    public async Task GetByStock_ReturnsOnlyFilingsForThatStock()
    {
        var voo = CreateStock("VOO", "0000036405");
        var other = CreateStock("SPY", "0000884394");
        _dbContext.Set<CommonStock>().AddRange(voo, other);
        await _dbContext.SaveChangesAsync();

        _repository.Add(CreateFiling(voo.Id, "0000036405-24-000002"));
        _repository.Add(CreateFiling(voo.Id, "0000036405-23-000003"));
        _repository.Add(CreateFiling(other.Id, "0000884394-24-000001"));
        await _repository.SaveChanges();

        var result = await _repository.GetByStock(voo).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(f => f.CommonStockId == voo.Id);
    }

    [Fact]
    public async Task GetByAccessionNumber_ExistingAccession_ReturnsFiling()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(CreateFiling(stock.Id, "0000036405-24-000002"));
        await _repository.SaveChanges();

        var result = await _repository
            .GetByAccessionNumber("0000036405-24-000002")
            .FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result.SeriesName.Should().Be("Vanguard 500 Index Fund");
    }

    [Fact]
    public async Task GetByAccessionNumber_NonExistentAccession_ReturnsEmpty()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(CreateFiling(stock.Id, "0000036405-24-000002"));
        await _repository.SaveChanges();

        var any = await _repository.GetByAccessionNumber("9999999999-99-999999").AnyAsync();

        any.Should().BeFalse();
    }

    [Fact]
    public async Task GetRecent_ReturnsOnlyFilingsOnOrAfterCutoff()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        _repository.Add(CreateFiling(stock.Id, "old", filingDate: new DateOnly(2024, 1, 1)));
        _repository.Add(CreateFiling(stock.Id, "new", filingDate: new DateOnly(2025, 1, 15)));
        await _repository.SaveChanges();

        var result = await _repository.GetRecent(new DateOnly(2025, 1, 1)).ToListAsync();

        result.Should().ContainSingle();
        result[0].AccessionNumber.Should().Be("new");
    }

    [Fact]
    public async Task Add_FilingWithHoldings_PersistsChildRows()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var filing = CreateFiling(stock.Id);
        filing.Holdings.Add(
            new NportHolding
            {
                Name = "AT&T Inc",
                Title = "AT&T Inc",
                Cusip = "00206R102",
                Isin = "US00206R1023",
                Balance = 112500m,
                Units = "NS",
                Currency = "USD",
                ValueUsd = 1_794_375m,
                PercentValue = 0.49m,
                PayoffProfile = "Long",
                AssetCategory = "EC",
                IssuerCategory = "CORP",
                InvestmentCountry = "US",
            }
        );
        filing.Holdings.Add(
            new NportHolding
            {
                Name = "US Treasury Note",
                Balance = 5_000_000m,
                Units = "PA",
                Currency = "USD",
                ValueUsd = 4_900_000m,
                PercentValue = 4.26m,
                PayoffProfile = "Long",
                AssetCategory = "DBT",
                IssuerCategory = "UST",
            }
        );
        _repository.Add(filing);
        await _repository.SaveChanges();

        var loaded = await _repository
            .GetByAccessionNumber(filing.AccessionNumber)
            .Include(f => f.Holdings)
            .FirstAsync();

        loaded.Holdings.Should().HaveCount(2);
        loaded
            .Holdings.Should()
            .Contain(h =>
                h.Name == "AT&T Inc" && h.Cusip == "00206R102" && h.AssetCategory == "EC"
            );
    }

    [Fact]
    public async Task GetLatestPerSeries_ReturnsTheNewestReportPerSeries()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var older = CreateFiling(stock.Id, "0000036405-24-000001", new DateOnly(2024, 11, 20));
        older.ReportPeriodDate = new DateOnly(2024, 10, 31);
        var newest = CreateFiling(stock.Id, "0000036405-25-000002", new DateOnly(2025, 1, 15));
        newest.ReportPeriodDate = new DateOnly(2024, 12, 31);
        var otherSeries = CreateFiling(
            stock.Id,
            "0000036405-25-000003",
            new DateOnly(2025, 1, 15),
            "Vanguard Growth Index Fund"
        );
        otherSeries.ReportPeriodDate = new DateOnly(2024, 12, 31);
        var belowFloor = CreateFiling(
            stock.Id,
            "0000036405-23-000004",
            new DateOnly(2023, 1, 15),
            "Vanguard Stale Fund"
        );
        belowFloor.ReportPeriodDate = new DateOnly(2022, 12, 31);
        _repository.Add(older);
        _repository.Add(newest);
        _repository.Add(otherSeries);
        _repository.Add(belowFloor);
        await _repository.SaveChanges();

        var result = await _repository.GetLatestPerSeries(new DateOnly(2024, 1, 1)).ToListAsync();

        result.Should().HaveCount(2);
        result.Select(f => f.Id).Should().BeEquivalentTo([newest.Id, otherSeries.Id]);
    }

    [Fact]
    public async Task GetHoldingsByCusip_ReturnsOnlyRowsCarryingThatCusip()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        var filing = CreateFiling(stock.Id);
        filing.Holdings.Add(
            new NportHolding
            {
                Name = "APPLE INC",
                Cusip = "037833100",
                Balance = 100m,
                Units = "NS",
                Currency = "USD",
                ValueUsd = 25_000m,
                PercentValue = 2.17m,
                PayoffProfile = "Long",
                AssetCategory = "EC",
                IssuerCategory = "CORP",
            }
        );
        filing.Holdings.Add(
            new NportHolding
            {
                Name = "MICROSOFT CORP",
                Cusip = "594918104",
                Balance = 50m,
                Units = "NS",
                Currency = "USD",
                ValueUsd = 21_000m,
                PercentValue = 1.83m,
                PayoffProfile = "Long",
                AssetCategory = "EC",
                IssuerCategory = "CORP",
            }
        );
        _repository.Add(filing);
        await _repository.SaveChanges();

        var result = await _repository.GetHoldingsByCusip("037833100").ToListAsync();

        result.Should().ContainSingle().Which.Name.Should().Be("APPLE INC");
    }
}
