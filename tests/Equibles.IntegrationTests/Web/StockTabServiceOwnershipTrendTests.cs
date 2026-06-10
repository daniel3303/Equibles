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
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the per-quarter institutional ownership trend on the holdings tab:
/// one point per report date ordered oldest first, shares summed over every
/// row (share classes included) and holders counted distinct — matching the
/// header-stat semantics for the latest quarter.
/// </summary>
public class StockTabServiceOwnershipTrendTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly StockTabService _sut;

    public StockTabServiceOwnershipTrendTests()
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
    public async Task LoadHoldingsTab_TwoQuarters_BuildsAscendingTrendWithDistinctHolderCounts()
    {
        var stock = NewStock();
        var holderA = NewHolder("Alpha", "1");
        var holderB = NewHolder("Beta", "2");
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(holderA, holderB);

        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                // prior quarter: holder A split across two share classes — shares
                // sum across rows, but the holder counts once.
                Make(stock.Id, holderA.Id, prior, 100, "cl-a"),
                Make(stock.Id, holderA.Id, prior, 40, "cl-b"),
                // current quarter: both holders.
                Make(stock.Id, holderA.Id, current, 150, "cl-a"),
                Make(stock.Id, holderB.Id, current, 50, "cl-a")
            );
        await _dbContext.SaveChangesAsync();

        var result = await _sut.LoadHoldingsTab(stock, date: null);

        result.OwnershipTrend.Should().HaveCount(2);
        result.OwnershipTrend[0].ReportDate.Should().Be(prior, "oldest first");
        result.OwnershipTrend[0].TotalShares.Should().Be(140);
        result.OwnershipTrend[0].HolderCount.Should().Be(1, "share classes are one holder");
        result.OwnershipTrend[1].ReportDate.Should().Be(current);
        result.OwnershipTrend[1].TotalShares.Should().Be(200);
        result.OwnershipTrend[1].HolderCount.Should().Be(2);

        // The latest trend point must agree with the header stats.
        result.OwnershipTrend[1].TotalShares.Should().Be(result.TotalShares);
        result.OwnershipTrend[1].HolderCount.Should().Be(result.HolderCount);
    }

    [Fact]
    public async Task LoadHoldingsCombinedTab_TwoQuarters_CarriesTheSameTrend()
    {
        var stock = NewStock();
        var holder = NewHolder("Alpha", "1");
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().Add(holder);

        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Make(stock.Id, holder.Id, prior, 100, "cl-a"),
                Make(stock.Id, holder.Id, current, 150, "cl-a")
            );
        await _dbContext.SaveChangesAsync();

        var result = await _sut.LoadHoldingsCombinedTab(stock);

        result.OwnershipTrend.Should().HaveCount(2);
        result.OwnershipTrend.Select(p => p.ReportDate).Should().Equal(prior, current);
        result.OwnershipTrend.Select(p => p.TotalShares).Should().Equal(100, 150);
    }

    [Fact]
    public async Task LoadHoldingsTab_NoHoldings_LeavesTrendEmpty()
    {
        var stock = NewStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.LoadHoldingsTab(stock, date: null);

        result.OwnershipTrend.Should().BeEmpty();
    }

    private static CommonStock NewStock() =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };

    private static InstitutionalHolder NewHolder(string name, string cik) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Cik = cik,
        };

    private static InstitutionalHolding Make(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        string shareClass
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = shares,
            Value = shares * 10,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber =
                $"acc-{stockId:N}".Substring(0, 12)
                + $"-{holderId:N}".Substring(0, 8)
                + $"-{reportDate:yyyyMMdd}-{shareClass}",
        };
}
