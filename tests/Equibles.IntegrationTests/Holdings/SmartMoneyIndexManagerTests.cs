using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;

namespace Equibles.IntegrationTests.Holdings;

public class SmartMoneyIndexManagerTests : IDisposable
{
    private static readonly DateOnly AsOf = new(2026, 1, 2);
    private static readonly DateOnly ReportDate = new(2025, 9, 30);

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly SmartMoneyIndexManager _manager;

    public SmartMoneyIndexManagerTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new HoldingsModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _manager = new SmartMoneyIndexManager(
            new FundScoreRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new InstitutionalHoldingRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext)
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task Build_TopFundsWithConsensus_BuildsEqualWeightedIndexAndTracksPerformance()
    {
        SeedBenchmark();
        var doubler = AddStock("AAA", "Alpha Co", start: 100m, end: 200m); // doubles
        var flat = AddStock("BBB", "Beta Co", start: 100m, end: 100m); // flat
        var solo = AddStock("CCC", "Gamma Co", start: 100m, end: 100m);

        // Three top funds. AAA held by all three, BBB by two, CCC by one.
        SeedFund("0000000001", alpha: 30m, (doubler, 60), (flat, 40));
        SeedFund("0000000002", alpha: 20m, (doubler, 50), (solo, 50));
        SeedFund("0000000003", alpha: 10m, (doubler, 70), (flat, 30));

        var result = await _manager.Build(
            AsOf,
            topFunds: 10,
            maxConstituents: 25,
            minConsensus: 2,
            windowYears: 3,
            benchmarkTicker: "SPY"
        );

        result.Reason.Should().BeNull();
        result.FundCount.Should().Be(3);
        result.ConstructionDate.Should().Be(ReportDate);

        // CCC is held by a single fund and falls below the consensus threshold.
        result.Constituents.Select(c => c.Ticker).Should().BeEquivalentTo(["AAA", "BBB"]);
        result.Constituents.Should().OnlyContain(c => c.IndexWeightPercent == 50m);
        result.Constituents.Single(c => c.Ticker == "AAA").HeldByCount.Should().Be(3);
        result.Constituents.Single(c => c.Ticker == "BBB").HeldByCount.Should().Be(2);

        // Equal-weighted basket: AAA doubles (+100%), BBB flat (0%) => ~+50% over the window.
        result.Backtest.Points.Should().NotBeEmpty();
        result.Backtest.PortfolioSummary.TotalReturnPercent.Should().BeApproximately(50m, 1m);
        result.Backtest.BenchmarkSummary.TotalReturnPercent.Should().BeApproximately(0m, 0.01m);
    }

    [Fact]
    public async Task Build_UnknownBenchmark_ReturnsReasonAndNoConstituents()
    {
        SeedBenchmark();

        var result = await _manager.Build(AsOf, benchmarkTicker: "NOSUCHTICKER");

        result.Constituents.Should().BeEmpty();
        result.Reason.Should().Contain("not known");
    }

    [Fact]
    public async Task Build_NoFundScores_ReturnsReason()
    {
        SeedBenchmark();

        var result = await _manager.Build(AsOf, benchmarkTicker: "SPY");

        result.Constituents.Should().BeEmpty();
        result.FundCount.Should().Be(0);
        result.Reason.Should().Contain("No fund scores");
    }

    [Fact]
    public async Task Build_NoStockMeetsConsensus_ReturnsReason()
    {
        SeedBenchmark();
        var a = AddStock("AAA", "Alpha Co", 100m, 200m);
        var b = AddStock("BBB", "Beta Co", 100m, 100m);

        // Two funds with entirely disjoint portfolios — nothing is held by both.
        SeedFund("0000000001", alpha: 30m, (a, 100));
        SeedFund("0000000002", alpha: 20m, (b, 100));

        var result = await _manager.Build(AsOf, minConsensus: 2, benchmarkTicker: "SPY");

        result.FundCount.Should().Be(2);
        result.Constituents.Should().BeEmpty();
        result.Reason.Should().Contain("No stock");
    }

    [Fact]
    public async Task Build_TopFundsHaveNoHoldingsOnFile_ReturnsReasonAndZeroFundCount()
    {
        // A scored fund exists and resolves, but has zero 13F holdings on file,
        // so LoadLatestPortfolio yields nothing and fundPortfolios stays empty.
        // This is distinct from "no scores": the fund ranked, it just has no portfolio.
        SeedBenchmark();
        SeedFund("0000000001", alpha: 30m);

        var result = await _manager.Build(AsOf, benchmarkTicker: "SPY");

        result.FundCount.Should().Be(0);
        result.Constituents.Should().BeEmpty();
        result.Reason.Should().Contain("no holdings on file");
    }

    private void SeedBenchmark()
    {
        AddStock("SPY", "S&P 500 ETF", start: 100m, end: 100m);
    }

    private CommonStock AddStock(string ticker, string name, decimal start, decimal end)
    {
        var stock = new CommonStock { Ticker = ticker, Name = name };
        _dbContext.Set<CommonStock>().Add(stock);
        AddPrice(stock, new DateOnly(2025, 11, 1), start);
        AddPrice(stock, AsOf, end);
        _dbContext.SaveChanges();
        return stock;
    }

    private InstitutionalHolder SeedFund(
        string cik,
        decimal alpha,
        params (CommonStock Stock, long Value)[] positions
    )
    {
        var holder = new InstitutionalHolder { Cik = cik, Name = $"Fund {cik}" };
        _dbContext.Set<InstitutionalHolder>().Add(holder);

        foreach (var (stock, value) in positions)
        {
            _dbContext
                .Set<InstitutionalHolding>()
                .Add(
                    new InstitutionalHolding
                    {
                        InstitutionalHolderId = holder.Id,
                        CommonStockId = stock.Id,
                        ReportDate = ReportDate,
                        FilingDate = ReportDate.AddDays(20),
                        Shares = value,
                        Value = value,
                    }
                );
        }

        _dbContext
            .Set<FundScore>()
            .Add(
                new FundScore
                {
                    InstitutionalHolderId = holder.Id,
                    WindowYears = 3,
                    BenchmarkTicker = "SPY",
                    WindowStart = new DateOnly(2023, 1, 2),
                    WindowEnd = AsOf,
                    AlphaPercent = alpha,
                }
            );

        _dbContext.SaveChanges();
        return holder;
    }

    private void AddPrice(CommonStock stock, DateOnly date, decimal close)
    {
        _dbContext
            .Set<DailyStockPrice>()
            .Add(
                new DailyStockPrice
                {
                    CommonStockId = stock.Id,
                    Date = date,
                    Open = close,
                    High = close,
                    Low = close,
                    Close = close,
                    AdjustedClose = close,
                }
            );
    }
}
