using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.InsiderTrading;

public class Form144FilingRepositoryTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly Form144FilingRepository _repository;

    public Form144FilingRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new InsiderTradingModuleConfiguration()
        );
        _repository = new Form144FilingRepository(_dbContext);
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

    private static Form144Filing CreateFiling(
        Guid commonStockId,
        string accessionNumber = "0001921094-26-000555",
        DateOnly? filingDate = null,
        string sellerName = "LEVINSON ARTHUR D"
    )
    {
        return new Form144Filing
        {
            Id = Guid.NewGuid(),
            CommonStockId = commonStockId,
            AccessionNumber = accessionNumber,
            FilingDate = filingDate ?? new DateOnly(2026, 5, 27),
            SellerName = sellerName,
            RelationshipToIssuer = "Director",
            SecurityClassTitle = "Common",
            BrokerName = "Charles Schwab & Co., Inc.",
            SharesToBeSold = 50000,
            AggregateMarketValue = 15551085.00m,
            SharesOutstanding = 14687356000,
            ApproxSaleDate = new DateOnly(2026, 5, 27),
            SecuritiesExchangeName = "NASDAQ",
        };
    }

    [Fact]
    public async Task GetByStock_ReturnsOnlyFilingsForThatStock()
    {
        var apple = CreateStock("AAPL", "0000320193");
        var microsoft = CreateStock("MSFT", "0000789019");
        _dbContext.Set<CommonStock>().AddRange(apple, microsoft);
        await _dbContext.SaveChangesAsync();

        _repository.Add(CreateFiling(apple.Id, "0001921094-26-000555"));
        _repository.Add(CreateFiling(apple.Id, "0001921094-26-000446"));
        _repository.Add(CreateFiling(microsoft.Id, "0001950047-26-004044"));
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
        _repository.Add(CreateFiling(stock.Id, "0001921094-26-000555"));
        await _repository.SaveChanges();

        var result = await _repository
            .GetByAccessionNumber("0001921094-26-000555")
            .FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result.SellerName.Should().Be("LEVINSON ARTHUR D");
    }

    [Fact]
    public async Task GetByAccessionNumber_NonExistentAccession_ReturnsEmpty()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(CreateFiling(stock.Id, "0001921094-26-000555"));
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

        _repository.Add(CreateFiling(stock.Id, "old", filingDate: new DateOnly(2026, 1, 1)));
        _repository.Add(CreateFiling(stock.Id, "new", filingDate: new DateOnly(2026, 5, 27)));
        await _repository.SaveChanges();

        var result = await _repository.GetRecent(new DateOnly(2026, 5, 1)).ToListAsync();

        result.Should().ContainSingle();
        result[0].AccessionNumber.Should().Be("new");
    }

    [Fact]
    public async Task Add_FilingWithPriorSales_PersistsChildRows()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var filing = CreateFiling(stock.Id);
        filing.PriorSales.Add(
            new Form144PriorSale
            {
                SellerName = "ARTHUR D LEVINSON",
                SecurityClassTitle = "Common",
                SaleDate = new DateOnly(2026, 5, 6),
                AmountSold = 250000,
                GrossProceeds = 71190164.00m,
            }
        );
        _repository.Add(filing);
        await _repository.SaveChanges();

        var loaded = await _repository
            .GetByAccessionNumber(filing.AccessionNumber)
            .Include(f => f.PriorSales)
            .FirstAsync();

        loaded.PriorSales.Should().ContainSingle();
        loaded.PriorSales[0].AmountSold.Should().Be(250000);
        loaded.PriorSales[0].GrossProceeds.Should().Be(71190164.00m);
    }
}
