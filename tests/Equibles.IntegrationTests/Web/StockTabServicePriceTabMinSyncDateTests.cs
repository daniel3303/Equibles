using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data;
using Equibles.Congress.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.Services;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.Options;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the Worker:MinSyncDate clamp on the price tab: rows before the
/// configured backfill floor are partial (the scrapers never revisit them), so
/// they must not feed the chart or the derived indicator series; with no floor
/// configured the full history renders unchanged.
/// </summary>
public class StockTabServicePriceTabMinSyncDateTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;

    public StockTabServicePriceTabMinSyncDateTests()
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
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task LoadPriceTab_PricesBeforeMinSyncDate_AreExcludedFromSeries()
    {
        var stock = SeedStockWithPricesAround(new DateOnly(2024, 6, 1));

        var sut = CreateService(new WorkerOptions { MinSyncDate = new DateTime(2024, 6, 1) });
        var result = await sut.LoadPriceTab(stock);

        result.Prices.Should().HaveCount(5, "only the floor date and later render");
        result.Prices.First().Date.Should().Be(new DateOnly(2024, 6, 1), "the floor is inclusive");
        // Derived series must align with the clamped prices, not the full history.
        result.Sma20.Should().HaveCount(5);
        result.Rsi14.Should().HaveCount(5);
    }

    [Fact]
    public async Task LoadPriceTab_NoMinSyncDateConfigured_RendersFullHistory()
    {
        var stock = SeedStockWithPricesAround(new DateOnly(2024, 6, 1));

        var sut = CreateService(workerOptions: null);
        var result = await sut.LoadPriceTab(stock);

        result.Prices.Should().HaveCount(10, "no floor means no clamp");
        result.Prices.First().Date.Should().Be(new DateOnly(2024, 5, 27));
    }

    // Seeds 5 consecutive daily rows before the pivot date and 5 from it onward.
    private CommonStock SeedStockWithPricesAround(DateOnly pivot)
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        _dbContext.Set<CommonStock>().Add(stock);
        for (var i = -5; i < 5; i++)
        {
            _dbContext
                .Set<DailyStockPrice>()
                .Add(
                    new DailyStockPrice
                    {
                        CommonStockId = stock.Id,
                        Date = pivot.AddDays(i),
                        Open = 100m,
                        High = 102m,
                        Low = 99m,
                        Close = 101m,
                        AdjustedClose = 101m,
                        Volume = 1_000_000,
                    }
                );
        }
        _dbContext.SaveChanges();
        return stock;
    }

    private StockTabService CreateService(WorkerOptions workerOptions) =>
        new(
            new InstitutionalHoldingRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new DailyShortVolumeRepository(_dbContext),
            new ShortInterestRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new DocumentRepository(_dbContext),
            new InsiderTransactionRepository(_dbContext),
            new Form144FilingRepository(_dbContext),
            new FormDFilingRepository(_dbContext),
            new NCenFilingRepository(_dbContext),
            new NportFilingRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            new FinancialConceptRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            workerOptions == null ? null : Options.Create(workerOptions)
        );
}
