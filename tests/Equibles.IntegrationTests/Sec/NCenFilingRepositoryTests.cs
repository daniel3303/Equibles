using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Sec;

public class NCenFilingRepositoryTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly NCenFilingRepository _repository;

    public NCenFilingRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        _repository = new NCenFilingRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static CommonStock CreateStock(string ticker = "MXF", string cik = "0000065433")
    {
        return new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
            Cik = cik,
        };
    }

    private static NCenFiling CreateFiling(
        Guid commonStockId,
        string accessionNumber = "0000065433-24-000002",
        DateOnly? filingDate = null,
        string registrantName = "MEXICO FUND INC"
    )
    {
        return new NCenFiling
        {
            Id = Guid.NewGuid(),
            CommonStockId = commonStockId,
            AccessionNumber = accessionNumber,
            FilingDate = filingDate ?? new DateOnly(2025, 1, 15),
            IsAmendment = false,
            RegistrantName = registrantName,
            InvestmentCompanyType = "N-2",
            InvestmentCompanyFileNumber = "811-02409",
            RegistrantLei = "00000000000000238096",
            State = "US-MD",
            Country = "US",
            ReportEndingPeriod = new DateOnly(2024, 10, 31),
            IsReportPeriodLessThan12Months = false,
            IsFirstFiling = false,
            IsLastFiling = false,
            IsFamilyInvestmentCompany = false,
        };
    }

    [Fact]
    public async Task GetByStock_ReturnsOnlyFilingsForThatStock()
    {
        var mexico = CreateStock("MXF", "0000065433");
        var other = CreateStock("ASA", "0000004969");
        _dbContext.Set<CommonStock>().AddRange(mexico, other);
        await _dbContext.SaveChangesAsync();

        _repository.Add(CreateFiling(mexico.Id, "0000065433-24-000002"));
        _repository.Add(CreateFiling(mexico.Id, "0000065433-23-000003"));
        _repository.Add(CreateFiling(other.Id, "0000004969-24-000001"));
        await _repository.SaveChanges();

        var result = await _repository.GetByStock(mexico).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(f => f.CommonStockId == mexico.Id);
    }

    [Fact]
    public async Task GetByAccessionNumber_ExistingAccession_ReturnsFiling()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(CreateFiling(stock.Id, "0000065433-24-000002"));
        await _repository.SaveChanges();

        var result = await _repository
            .GetByAccessionNumber("0000065433-24-000002")
            .FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result.RegistrantName.Should().Be("MEXICO FUND INC");
    }

    [Fact]
    public async Task GetByAccessionNumber_NonExistentAccession_ReturnsEmpty()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(CreateFiling(stock.Id, "0000065433-24-000002"));
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
    public async Task Add_FilingWithServiceProviders_PersistsChildRows()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var filing = CreateFiling(stock.Id);
        filing.ServiceProviders.Add(
            new NCenServiceProvider
            {
                ProviderType = NCenServiceProviderType.InvestmentAdviser,
                Name = "IMPULSORA DEL FONDO MEXICO SC",
                Country = "MX",
                IsAffiliated = false,
            }
        );
        filing.ServiceProviders.Add(
            new NCenServiceProvider
            {
                ProviderType = NCenServiceProviderType.PublicAccountant,
                Name = "TAIT, WELLER & BAKER LLP",
                Country = "US",
                IsAffiliated = false,
            }
        );
        _repository.Add(filing);
        await _repository.SaveChanges();

        var loaded = await _repository
            .GetByAccessionNumber(filing.AccessionNumber)
            .Include(f => f.ServiceProviders)
            .FirstAsync();

        loaded.ServiceProviders.Should().HaveCount(2);
        loaded
            .ServiceProviders.Should()
            .Contain(p =>
                p.ProviderType == NCenServiceProviderType.InvestmentAdviser
                && p.Name == "IMPULSORA DEL FONDO MEXICO SC"
            );
    }
}
