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
