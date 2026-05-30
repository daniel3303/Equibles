using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
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
/// Pins the Sold-Out classification path in LoadHoldingsTab (previously
/// uncovered): a holder absent from the current quarter for this stock counts
/// as Sold Out only when it actually filed a 13F this quarter (proving it's an
/// active filer that exited), not merely missing. Exercises the gap-holder query.
/// </summary>
public class StockTabServiceLoadHoldingsTabSoldOutTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly StockTabService _sut;

    public StockTabServiceLoadHoldingsTabSoldOutTests()
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
            new Form144FilingRepository(_dbContext),
            new FormDFilingRepository(_dbContext),
            new NCenFilingRepository(_dbContext),
            new NportFilingRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            new FinancialConceptRepository(_dbContext),
            new CommonStockRepository(_dbContext)
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task LoadHoldingsTab_ExitedHolderStillFilingThisQuarter_CountsAsSoldOut()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.", "0000320193");
        var msft = MakeStock("MSFT", "Microsoft Corp.", "0000789019");
        _dbContext.Set<CommonStock>().AddRange(aapl, msft);

        var exiting = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Name = "Exiting",
            Cik = "1",
        };
        var staying = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Name = "Staying",
            Cik = "2",
        };
        _dbContext.Set<InstitutionalHolder>().AddRange(exiting, staying);

        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        // Prior quarter: both hold AAPL.
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Make(aapl.Id, exiting.Id, prior, 100, 1_000_000));
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Make(aapl.Id, staying.Id, prior, 200, 2_000_000));
        // Current quarter: staying keeps AAPL; exiting dropped AAPL but still filed (MSFT).
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Make(aapl.Id, staying.Id, current, 200, 2_000_000));
        _dbContext.Set<InstitutionalHolding>().Add(Make(msft.Id, exiting.Id, current, 50, 500_000));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.LoadHoldingsTab(aapl, date: null);

        // The exiting holder filed elsewhere this quarter, so it's a genuine exit.
        result.BucketCounts.GetValueOrDefault(PositionChangeType.SoldOut).Should().Be(1);
    }

    private static CommonStock MakeStock(string ticker, string name, string cik) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = cik,
        };

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
