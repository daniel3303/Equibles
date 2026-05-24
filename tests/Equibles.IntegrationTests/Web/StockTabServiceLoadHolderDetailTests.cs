using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data;
using Equibles.Congress.Repositories;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.Services;
using Equibles.Web.ViewModels.Stocks;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Every StockTabService Load* method has tests except <c>LoadHolderDetail</c>,
/// which was 14/14 lines zero-hit in the local cobertura baseline. It backs the
/// `~/Stocks/{ticker}/Holders/{cik}` page. This pins its two load-bearing
/// clauses: the <c>InstitutionalHolderId == holder.Id</c> filter (must NOT leak
/// other holders' positions in the same stock) and the ReportDate-descending
/// order. A regression in either silently shows the wrong fund's holdings.
/// </summary>
public class StockTabServiceLoadHolderDetailTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;
    private readonly StockTabService _service;

    public StockTabServiceLoadHolderDetailTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new FinancialFactsModuleConfiguration(),
            new FinraModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _service = new StockTabService(
            new InstitutionalHoldingRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new DailyShortVolumeRepository(_dbContext),
            new ShortInterestRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new DocumentRepository(_dbContext),
            new InsiderTransactionRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            new FinancialConceptRepository(_dbContext)
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task LoadHolderDetail_FiltersToHolderAndOrdersByReportDateDescending()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var holder = new InstitutionalHolder { Cik = "0001067983", Name = "Berkshire" };
        var otherHolder = new InstitutionalHolder { Cik = "0000102909", Name = "Vanguard" };
        _dbContext.AddRange(stock, holder, otherHolder);

        InstitutionalHolding Holding(InstitutionalHolder h, DateOnly report, long shares) =>
            new()
            {
                CommonStockId = stock.Id,
                InstitutionalHolderId = h.Id,
                FilingDate = report.AddDays(30),
                ReportDate = report,
                Value = shares * 10,
                Shares = shares,
            };

        _dbContext.AddRange(
            Holding(holder, new DateOnly(2024, 9, 30), 100),
            Holding(holder, new DateOnly(2024, 12, 31), 150),
            // Same stock, different holder — must be excluded by the holder filter.
            Holding(otherHolder, new DateOnly(2024, 12, 31), 999)
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadHolderDetail(stock, holder);

        result.Should().BeOfType<HolderDetailViewModel>();
        result.Stock.Ticker.Should().Be("AAPL");
        result.Holder.Name.Should().Be("Berkshire");
        result.Holdings.Should().HaveCount(2);
        result
            .Holdings.Select(h => h.ReportDate)
            .Should()
            .ContainInOrder(new DateOnly(2024, 12, 31), new DateOnly(2024, 9, 30));
        result.Holdings.Should().OnlyContain(h => h.InstitutionalHolderId == holder.Id);
    }
}
