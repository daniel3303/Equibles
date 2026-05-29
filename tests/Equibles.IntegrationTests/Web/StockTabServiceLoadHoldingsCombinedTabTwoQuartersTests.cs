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
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins LoadHoldingsCombinedTab's data-present path (only the fewer-than-two-
/// quarters unavailable path was covered): with two quarters it must mark the
/// combined view available and assemble one row per holder — the latest filing
/// for holders who refiled, the prior-quarter fallback for those who didn't.
/// </summary>
public class StockTabServiceLoadHoldingsCombinedTabTwoQuartersTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly StockTabService _sut;

    public StockTabServiceLoadHoldingsCombinedTabTwoQuartersTests()
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

        _sut = new StockTabService(
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
    public async Task LoadHoldingsCombinedTab_TwoQuarters_TakesLatestPositionPerHolder()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        _dbContext.Set<CommonStock>().Add(stock);

        var refiled = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Name = "Refiled",
            Cik = "1",
        };
        var newFiler = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Name = "NewFiler",
            Cik = "2",
        };
        var droppedOff = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Name = "Dropped",
            Cik = "3",
        };
        _dbContext.Set<InstitutionalHolder>().AddRange(refiled, newFiler, droppedOff);

        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        // refiled: both quarters -> combined keeps the current (150) row, not the prior.
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Make(stock.Id, refiled.Id, prior, 100, 1_000_000));
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Make(stock.Id, refiled.Id, current, 150, 1_500_000));
        // newFiler: current only.
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Make(stock.Id, newFiler.Id, current, 50, 500_000));
        // droppedOff: prior only -> combined falls back to the prior (80) row.
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Make(stock.Id, droppedOff.Id, prior, 80, 800_000));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.LoadHoldingsCombinedTab(stock);

        result.IsCombinedView.Should().BeTrue();
        result.IsCombinedAvailable.Should().BeTrue("two quarters exist");
        result.HolderCount.Should().Be(3); // one row per distinct holder
        result.TotalShares.Should().Be(280); // 150 (current) + 50 (current) + 80 (prior fallback)
    }

    private static InstitutionalHolding Make(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber =
                $"acc-{stockId:N}".Substring(0, 12)
                + $"-{holderId:N}".Substring(0, 8)
                + $"-{reportDate:yyyyMMdd}",
        };
}
