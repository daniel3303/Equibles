using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.Services;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.Options;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the Worker:MinSyncDate clamp on the ownership/event tabs: holdings
/// report dates (selector, stats, and ownership trend), insider transactions,
/// and congressional trades all exclude rows before the backfill floor, while
/// an unset floor leaves the history unchanged.
/// </summary>
public class StockTabServiceEventTabsMinSyncDateTests : IDisposable
{
    private static readonly DateOnly Floor = new(2024, 6, 1);

    private readonly EquiblesFinancialDbContext _dbContext;

    public StockTabServiceEventTabsMinSyncDateTests()
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
    public async Task LoadHoldingsTab_QuarterBeforeMinSyncDate_DroppedFromDatesStatsAndTrend()
    {
        var stock = SeedStock();
        var holder = new InstitutionalHolder { Cik = "H0000001", Name = "Holder" };
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        var preFloorQuarter = new DateOnly(2024, 3, 31);
        var postFloorQuarter = new DateOnly(2024, 6, 30);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                MakeHolding(stock.Id, holder.Id, preFloorQuarter, 100),
                MakeHolding(stock.Id, holder.Id, postFloorQuarter, 150)
            );
        await _dbContext.SaveChangesAsync();

        var result = await CreateService(withFloor: true).LoadHoldingsTab(stock, date: null);

        result.AvailableDates.Should().Equal(postFloorQuarter);
        result.SelectedDate.Should().Be(postFloorQuarter);
        result
            .OwnershipTrend.Should()
            .ContainSingle()
            .Which.ReportDate.Should()
            .Be(postFloorQuarter);
    }

    [Fact]
    public async Task LoadInsiderTradingTab_TransactionBeforeMinSyncDate_IsExcluded()
    {
        var stock = SeedStock();
        var owner = new InsiderOwner { Name = "Insider", OwnerCik = "I0000001" };
        _dbContext.Set<InsiderOwner>().Add(owner);
        _dbContext
            .Set<InsiderTransaction>()
            .AddRange(
                MakeTransaction(stock, owner, Floor.AddDays(-1)),
                MakeTransaction(stock, owner, Floor)
            );
        await _dbContext.SaveChangesAsync();

        var result = await CreateService(withFloor: true).LoadInsiderTradingTab(stock);

        result.Transactions.Should().ContainSingle("the pre-floor transaction is partial data");
        result.Transactions[0].TransactionDate.Should().Be(Floor, "the floor is inclusive");
    }

    [Fact]
    public async Task LoadCongressionalTradesTab_TradeBeforeMinSyncDate_IsExcluded()
    {
        var stock = SeedStock();
        var member = new CongressMember
        {
            Name = "Dan Crenshaw",
            Position = CongressPosition.Senator,
        };
        _dbContext.Set<CongressMember>().Add(member);
        _dbContext
            .Set<CongressionalTrade>()
            .AddRange(MakeTrade(stock, member, Floor.AddDays(-1)), MakeTrade(stock, member, Floor));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService(withFloor: true).LoadCongressionalTradesTab(stock);

        result.Trades.Should().ContainSingle();
        result.Trades[0].TransactionDate.Should().Be(Floor);
    }

    [Fact]
    public async Task LoadInsiderTradingTab_NoMinSyncDateConfigured_RendersFullHistory()
    {
        var stock = SeedStock();
        var owner = new InsiderOwner { Name = "Insider", OwnerCik = "I0000001" };
        _dbContext.Set<InsiderOwner>().Add(owner);
        _dbContext
            .Set<InsiderTransaction>()
            .AddRange(
                MakeTransaction(stock, owner, Floor.AddDays(-1)),
                MakeTransaction(stock, owner, Floor)
            );
        await _dbContext.SaveChangesAsync();

        var result = await CreateService(withFloor: false).LoadInsiderTradingTab(stock);

        result.Transactions.Should().HaveCount(2, "no floor means no clamp");
    }

    private CommonStock SeedStock()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        _dbContext.Set<CommonStock>().Add(stock);
        return stock;
    }

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares
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
            AccessionNumber = $"acc-{reportDate:yyyyMMdd}-0001",
        };

    private static InsiderTransaction MakeTransaction(
        CommonStock stock,
        InsiderOwner owner,
        DateOnly date
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InsiderOwnerId = owner.Id,
            TransactionDate = date,
            FilingDate = date.AddDays(2),
            TransactionCode = TransactionCode.Purchase,
            Shares = 100,
            PricePerShare = 10m,
            SecurityTitle = "Common Stock",
            AccessionNumber = $"acc-{date:yyyyMMdd}-ins",
        };

    private static CongressionalTrade MakeTrade(
        CommonStock stock,
        CongressMember member,
        DateOnly date
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            CongressMemberId = member.Id,
            TransactionDate = date,
            FilingDate = date.AddDays(10),
            TransactionType = CongressTransactionType.Purchase,
            AssetName = "Apple Inc. Common Stock",
            OwnerType = "self",
            AmountFrom = 1_001,
            AmountTo = 15_000,
        };

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
