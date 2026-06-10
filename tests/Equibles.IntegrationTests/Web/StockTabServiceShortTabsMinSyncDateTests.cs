using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data;
using Equibles.Congress.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.Services;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.Options;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the Worker:MinSyncDate clamp on the three short-data tabs (daily short
/// volume, short interest, fails-to-deliver): rows before the backfill floor
/// are partial and must not render; with no floor the history is unchanged.
/// </summary>
public class StockTabServiceShortTabsMinSyncDateTests : IDisposable
{
    private static readonly DateOnly Floor = new(2024, 6, 1);

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly CommonStock _stock;

    public StockTabServiceShortTabsMinSyncDateTests()
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

        _stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        _dbContext.Set<CommonStock>().Add(_stock);
        foreach (var date in new[] { Floor.AddDays(-7), Floor, Floor.AddDays(7) })
        {
            _dbContext
                .Set<DailyShortVolume>()
                .Add(
                    new DailyShortVolume
                    {
                        CommonStockId = _stock.Id,
                        Date = date,
                        ShortVolume = 500_000,
                        ShortExemptVolume = 1_000,
                        TotalVolume = 1_200_000,
                        Market = "NYSE",
                    }
                );
            _dbContext
                .Set<ShortInterest>()
                .Add(
                    new ShortInterest
                    {
                        CommonStockId = _stock.Id,
                        SettlementDate = date,
                        CurrentShortPosition = 10_000_000,
                        PreviousShortPosition = 9_500_000,
                        ChangeInShortPosition = 500_000,
                        AverageDailyVolume = 50_000_000,
                        DaysToCover = 0.2m,
                    }
                );
            _dbContext
                .Set<FailToDeliver>()
                .Add(
                    new FailToDeliver
                    {
                        CommonStockId = _stock.Id,
                        SettlementDate = date,
                        Quantity = 50_000,
                        Price = 150.25m,
                    }
                );
        }
        _dbContext.SaveChanges();
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task LoadShortVolumeTab_RowBeforeMinSyncDate_IsExcluded()
    {
        var result = await CreateService(withFloor: true).LoadShortVolumeTab(_stock);

        result.ShortVolumes.Should().HaveCount(2, "the pre-floor row is partial data");
        result.ShortVolumes.First().Date.Should().Be(Floor, "the floor is inclusive");
    }

    [Fact]
    public async Task LoadShortInterestTab_RowBeforeMinSyncDate_IsExcluded()
    {
        var result = await CreateService(withFloor: true).LoadShortInterestTab(_stock);

        result.ShortInterests.Should().HaveCount(2);
        result.ShortInterests.First().SettlementDate.Should().Be(Floor);
    }

    [Fact]
    public async Task LoadFtdTab_RowBeforeMinSyncDate_IsExcluded()
    {
        var result = await CreateService(withFloor: true).LoadFtdTab(_stock);

        result.FailsToDeliver.Should().HaveCount(2);
        result.FailsToDeliver.First().SettlementDate.Should().Be(Floor);
    }

    [Fact]
    public async Task LoadShortVolumeTab_NoMinSyncDateConfigured_RendersFullHistory()
    {
        var result = await CreateService(withFloor: false).LoadShortVolumeTab(_stock);

        result.ShortVolumes.Should().HaveCount(3, "no floor means no clamp");
    }

    private StockTabService CreateService(bool withFloor) =>
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
            withFloor
                ? Options.Create(
                    new WorkerOptions { MinSyncDate = Floor.ToDateTime(TimeOnly.MinValue) }
                )
                : null
        );
}
