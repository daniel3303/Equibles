using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Sec;

public class FormDFilingRepositoryTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly FormDFilingRepository _repository;

    public FormDFilingRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        _repository = new FormDFilingRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static CommonStock CreateStock(string ticker = "AAPL", string cik = "0000320193")
    {
        return new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
            Cik = cik,
        };
    }

    private static FormDFiling CreateFiling(
        Guid commonStockId,
        string accessionNumber = "0002058722-25-000001",
        DateOnly? filingDate = null,
        string entityName = "AJ BOULDER FUND LLC"
    )
    {
        return new FormDFiling
        {
            Id = Guid.NewGuid(),
            CommonStockId = commonStockId,
            AccessionNumber = accessionNumber,
            FilingDate = filingDate ?? new DateOnly(2025, 2, 28),
            IsAmendment = false,
            EntityName = entityName,
            EntityType = "Limited Liability Company",
            JurisdictionOfInc = "DELAWARE",
            YearOfIncorporation = 2024,
            IndustryGroup = "Pooled Investment Fund",
            FederalExemptions = "06b, 3C, 3C.7",
            TotalOfferingAmount = null,
            IsOfferingAmountIndefinite = true,
            TotalAmountSold = 0,
            TotalRemaining = null,
            IsRemainingIndefinite = true,
            MinimumInvestmentAccepted = 0,
            HasNonAccreditedInvestors = false,
            TotalNumberAlreadyInvested = 0,
        };
    }

    [Fact]
    public async Task GetByStock_ReturnsOnlyFilingsForThatStock()
    {
        var apple = CreateStock("AAPL", "0000320193");
        var microsoft = CreateStock("MSFT", "0000789019");
        _dbContext.Set<CommonStock>().AddRange(apple, microsoft);
        await _dbContext.SaveChangesAsync();

        _repository.Add(CreateFiling(apple.Id, "0002058722-25-000001"));
        _repository.Add(CreateFiling(apple.Id, "0002058722-25-000002"));
        _repository.Add(CreateFiling(microsoft.Id, "0001950047-25-004044"));
        await _repository.SaveChanges();

        var result = await _repository.GetByStock(apple).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(f => f.CommonStockId == apple.Id);
    }

    [Fact]
    public async Task GetByAccessionNumber_ExistingAccession_ReturnsFiling()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(CreateFiling(stock.Id, "0002058722-25-000001"));
        await _repository.SaveChanges();

        var result = await _repository
            .GetByAccessionNumber("0002058722-25-000001")
            .FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result.EntityName.Should().Be("AJ BOULDER FUND LLC");
    }

    [Fact]
    public async Task GetByAccessionNumber_NonExistentAccession_ReturnsEmpty()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(CreateFiling(stock.Id, "0002058722-25-000001"));
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

        _repository.Add(CreateFiling(stock.Id, "old", filingDate: new DateOnly(2025, 1, 1)));
        _repository.Add(CreateFiling(stock.Id, "new", filingDate: new DateOnly(2025, 5, 27)));
        await _repository.SaveChanges();

        var result = await _repository.GetRecent(new DateOnly(2025, 5, 1)).ToListAsync();

        result.Should().ContainSingle();
        result[0].AccessionNumber.Should().Be("new");
    }

    [Fact]
    public async Task Add_FilingWithRelatedPersons_PersistsChildRows()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var filing = CreateFiling(stock.Id);
        filing.RelatedPersons.Add(
            new FormDRelatedPerson
            {
                Name = "BENJAMIN WEPRIN",
                Relationships = "Executive Officer, Promoter",
            }
        );
        _repository.Add(filing);
        await _repository.SaveChanges();

        var loaded = await _repository
            .GetByAccessionNumber(filing.AccessionNumber)
            .Include(f => f.RelatedPersons)
            .FirstAsync();

        loaded.RelatedPersons.Should().ContainSingle();
        loaded.RelatedPersons[0].Name.Should().Be("BENJAMIN WEPRIN");
        loaded.RelatedPersons[0].Relationships.Should().Be("Executive Officer, Promoter");
    }
}
