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

public class FundScoringManagerTests : IDisposable
{
    private static readonly DateOnly AsOf = new(2026, 1, 2);
    private static readonly DateOnly WindowStart = new(2023, 1, 2); // AsOf minus 3 years

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly FundScoreRepository _fundScoreRepository;
    private readonly FundScoringManager _manager;

    public FundScoringManagerTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new HoldingsModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _holderRepository = new InstitutionalHolderRepository(_dbContext);
        _fundScoreRepository = new FundScoreRepository(_dbContext);
        _manager = new FundScoringManager(
            new InstitutionalHoldingRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            _fundScoreRepository
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task ScoreHolder_PortfolioDoublesAgainstFlatBenchmark_PersistsPositiveAlpha()
    {
        var holder = SeedDoublingPortfolioAgainstFlatBenchmark();

        var score = await _manager.ScoreHolder(
            holder,
            AsOf,
            windowYears: 3,
            benchmarkTicker: "SPY"
        );

        score.Should().NotBeNull();
        // The single held stock doubles over the window held buy-and-hold => ~+100% total return.
        score.PortfolioTotalReturnPercent.Should().BeApproximately(100m, 0.5m);
        // Flat benchmark => ~0% return, so the portfolio's whole CAGR is alpha.
        score.BenchmarkTotalReturnPercent.Should().BeApproximately(0m, 0.01m);
        score.PortfolioCagrPercent.Should().BeGreaterThan(0m);
        score.AlphaPercent.Should().Be(score.PortfolioCagrPercent - score.BenchmarkCagrPercent);
        score.AlphaPercent.Should().BeGreaterThan(0m);
        score.WindowStart.Should().Be(WindowStart);
        score.WindowEnd.Should().Be(AsOf);
        score.BenchmarkTicker.Should().Be("SPY");
        score.WindowYears.Should().Be(3);
    }

    [Fact]
    public async Task ScoreHolder_Recomputed_UpdatesInPlaceWithoutDuplicating()
    {
        var holder = SeedDoublingPortfolioAgainstFlatBenchmark();

        await _manager.ScoreHolder(holder, AsOf, windowYears: 3, benchmarkTicker: "SPY");
        await _manager.ScoreHolder(holder, AsOf, windowYears: 3, benchmarkTicker: "SPY");

        _fundScoreRepository.GetByHolder(holder).Should().HaveCount(1);
    }

    [Fact]
    public async Task ScoreHolder_UnknownBenchmark_ReturnsNull()
    {
        var holder = SeedDoublingPortfolioAgainstFlatBenchmark();

        var score = await _manager.ScoreHolder(
            holder,
            AsOf,
            windowYears: 3,
            benchmarkTicker: "NOSUCHTICKER"
        );

        score.Should().BeNull();
    }

    [Fact]
    public async Task ScoreHolder_HolderWithNoHoldings_ReturnsNull()
    {
        SeedBenchmark();
        var holder = new InstitutionalHolder { Cik = "0009999999", Name = "Empty Fund" };
        _holderRepository.Add(holder);
        await _holderRepository.SaveChanges();

        var score = await _manager.ScoreHolder(
            holder,
            AsOf,
            windowYears: 3,
            benchmarkTicker: "SPY"
        );

        score.Should().BeNull();
    }

    [Fact]
    public async Task ScoreHolder_Later13DGStakeOnMooningStock_ScoresThe13FPortfolioOnly()
    {
        var holder = SeedDoublingPortfolioAgainstFlatBenchmark();

        // A Schedule 13D stake filed after the last 13F quarter, on a stock that then 50x's.
        // Treated as a portfolio snapshot it would rotate the whole simulation into that stock;
        // it must be ignored because a 13D/G describes a single stake, not the fund's portfolio.
        var moon = new CommonStock { Ticker = "MOON", Name = "Mooning Co" };
        _dbContext.Set<CommonStock>().Add(moon);
        AddPrice(moon, new DateOnly(2025, 6, 1), 1m);
        AddPrice(moon, AsOf, 50m);
        Add13DStake(holder, moon, eventDate: new DateOnly(2025, 6, 1));

        var score = await _manager.ScoreHolder(
            holder,
            AsOf,
            windowYears: 3,
            benchmarkTicker: "SPY"
        );

        score.Should().NotBeNull();
        // Same result as without the 13D row: the 13F portfolio doubles => ~+100% total return.
        score.PortfolioTotalReturnPercent.Should().BeApproximately(100m, 0.5m);
    }

    [Fact]
    public async Task ScoreHolder_BenchmarkPricesMissing_ReturnsNullButKeepsExistingScore()
    {
        // Benchmark stock exists but has no prices in the window — a transient data gap, not
        // a structural "nothing to score". The previous score must survive the failed cycle;
        // pruning here would wipe the whole leaderboard whenever the price feed lags.
        var benchmark = new CommonStock { Ticker = "SPY", Name = "S&P 500 ETF" };
        _dbContext.Set<CommonStock>().Add(benchmark);

        var held = new CommonStock { Ticker = "AAA", Name = "Alpha Co" };
        _dbContext.Set<CommonStock>().Add(held);
        AddPrice(held, new DateOnly(2022, 12, 20), 100m);
        AddPrice(held, AsOf, 200m);

        var holder = new InstitutionalHolder { Cik = "0001234567", Name = "Doubler Capital" };
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(
                new InstitutionalHolding
                {
                    InstitutionalHolderId = holder.Id,
                    CommonStockId = held.Id,
                    ReportDate = new DateOnly(2022, 9, 30),
                    FilingDate = new DateOnly(2022, 11, 10),
                    Shares = 1000,
                    Value = 100_000,
                }
            );

        _dbContext
            .Set<FundScore>()
            .Add(
                new FundScore
                {
                    InstitutionalHolderId = holder.Id,
                    WindowYears = 3,
                    BenchmarkTicker = "SPY",
                    WindowStart = WindowStart,
                    WindowEnd = AsOf,
                    AlphaPercent = 42m,
                }
            );
        _dbContext.SaveChanges();

        var score = await _manager.ScoreHolder(
            holder,
            AsOf,
            windowYears: 3,
            benchmarkTicker: "SPY"
        );

        score.Should().BeNull();
        _fundScoreRepository.GetByHolder(holder).Should().ContainSingle(s => s.AlphaPercent == 42m);
    }

    [Fact]
    public async Task ScoreHolder_FilerWithOnly13DGStakes_ReturnsNullAndDeletesStaleScore()
    {
        SeedBenchmark();
        var moon = new CommonStock { Ticker = "MOON", Name = "Mooning Co" };
        _dbContext.Set<CommonStock>().Add(moon);
        AddPrice(moon, new DateOnly(2025, 6, 1), 1m);
        AddPrice(moon, AsOf, 50m);

        var activist = new InstitutionalHolder { Cik = "0007654321", Name = "Activist Person" };
        _dbContext.Set<InstitutionalHolder>().Add(activist);
        _dbContext.SaveChanges();
        Add13DStake(activist, moon, eventDate: new DateOnly(2025, 6, 1));

        // A score persisted before 13D/G rows were excluded from scoring.
        _dbContext
            .Set<FundScore>()
            .Add(
                new FundScore
                {
                    InstitutionalHolderId = activist.Id,
                    WindowYears = 3,
                    BenchmarkTicker = "SPY",
                    WindowStart = WindowStart,
                    WindowEnd = AsOf,
                    AlphaPercent = 444_167m,
                }
            );
        _dbContext.SaveChanges();

        var score = await _manager.ScoreHolder(
            activist,
            AsOf,
            windowYears: 3,
            benchmarkTicker: "SPY"
        );

        score.Should().BeNull();
        // The stale score is pruned, not just skipped — otherwise the leaderboard keeps it.
        _fundScoreRepository.GetByHolder(activist).Should().BeEmpty();
    }

    private void Add13DStake(InstitutionalHolder holder, CommonStock stock, DateOnly eventDate)
    {
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(
                new InstitutionalHolding
                {
                    InstitutionalHolderId = holder.Id,
                    CommonStockId = stock.Id,
                    ReportDate = eventDate,
                    FilingDate = eventDate.AddDays(5),
                    FilingType = FilingType.Schedule13D,
                    Shares = 1_000_000,
                    Value = 1_000_000,
                }
            );
        _dbContext.SaveChanges();
    }

    private InstitutionalHolder SeedDoublingPortfolioAgainstFlatBenchmark()
    {
        SeedBenchmark();

        var held = new CommonStock { Ticker = "AAA", Name = "Alpha Co" };
        _dbContext.Set<CommonStock>().Add(held);
        AddPrice(held, new DateOnly(2022, 12, 20), 100m);
        AddPrice(held, AsOf, 200m); // doubles over the window

        var holder = new InstitutionalHolder { Cik = "0001234567", Name = "Doubler Capital" };
        _dbContext.Set<InstitutionalHolder>().Add(holder);

        // Rebalance (ReportDate + 45 days) lands before the window start, so the portfolio is
        // held from day one of the window through to AsOf.
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(
                new InstitutionalHolding
                {
                    InstitutionalHolderId = holder.Id,
                    CommonStockId = held.Id,
                    ReportDate = new DateOnly(2022, 9, 30),
                    FilingDate = new DateOnly(2022, 11, 10),
                    Shares = 1000,
                    Value = 100_000,
                }
            );

        _dbContext.SaveChanges();
        return holder;
    }

    private void SeedBenchmark()
    {
        var benchmark = new CommonStock { Ticker = "SPY", Name = "S&P 500 ETF" };
        _dbContext.Set<CommonStock>().Add(benchmark);
        AddPrice(benchmark, new DateOnly(2022, 12, 20), 100m);
        AddPrice(benchmark, AsOf, 100m); // flat over the window
        _dbContext.SaveChanges();
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
