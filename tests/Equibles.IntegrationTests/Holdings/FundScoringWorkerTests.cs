using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract: <see cref="FundScoringWorker"/> enumerates every filer with a filing rollup on
/// file, scores each in its own scope, and persists the results.
/// </summary>
public class FundScoringWorkerTests : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // Start of the default 3-year window the worker scores. The seed anchors prices just inside
    // the price-load lookback so forward-fill resolves a base close at the window's first day.
    private static readonly DateOnly WindowFrom = Today.AddYears(-3);
    private static readonly DateOnly EarlyPriceDate = WindowFrom.AddDays(-10);

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly FundScoreRepository _fundScoreRepository;
    private readonly FundScoringWorker _worker;
    private EquityIssuer _benchmark;

    public FundScoringWorkerTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new HoldingsModuleConfiguration(),
            new CorporateActionsModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _fundScoreRepository = new FundScoreRepository(_dbContext);
        _worker = new FundScoringWorker(
            SharedContextScopeFactory(),
            NullLogger<FundScoringWorker>.Instance
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task ScoreAllHolders_PersistsAScorePerFilerWithHoldings()
    {
        var holder = SeedDoublingPortfolioAgainstFlatBenchmark();

        var scored = await _worker.ScoreAllHolders(CancellationToken.None);

        scored.Should().Be(1);
        var persisted = _fundScoreRepository.GetByHolder(holder).ToList();
        persisted.Should().HaveCount(1);
        persisted[0].AlphaPercent.Should().BeGreaterThan(0m);
        persisted[0].BenchmarkTicker.Should().Be("SPY");
        persisted[0].CalculationVersion.Should().Be(FundScore.CurrentCalculationVersion);
    }

    [Fact]
    public async Task ScoreAllHolders_RecomputesAFreshScoreFromAnOlderCalculationBasis()
    {
        var holder = SeedDoublingPortfolioAgainstFlatBenchmark();
        _fundScoreRepository.Add(
            new FundScore
            {
                InstitutionalHolderId = holder.Id,
                BenchmarkTicker = "SPY",
                WindowYears = FundScoringManager.DefaultWindowYears,
                CalculationVersion = FundScore.CurrentCalculationVersion - 1,
                AlphaPercent = -99m,
                CreationTime = DateTime.UtcNow.AddYears(1),
            }
        );
        await _fundScoreRepository.SaveChanges();

        var scored = await _worker.ScoreAllHolders(CancellationToken.None);

        scored.Should().Be(1);
        var persisted = _fundScoreRepository.GetByHolder(holder).Should().ContainSingle().Subject;
        persisted.CalculationVersion.Should().Be(FundScore.CurrentCalculationVersion);
        persisted.AlphaPercent.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task ScoreAllHolders_NoHoldings_ScoresNothing()
    {
        SeedBenchmark();

        var scored = await _worker.ScoreAllHolders(CancellationToken.None);

        scored.Should().Be(0);
        _fundScoreRepository.GetAll().Should().BeEmpty();
    }

    // Discovery reads the per-accession filing rollups, never the positions: positions whose
    // rollup has not landed yet belong to an in-progress import and are scored once it does.
    [Fact]
    public async Task ScoreAllHolders_HoldingsWithoutAFilingRollup_AreNotDiscovered()
    {
        SeedDoublingPortfolioAgainstFlatBenchmark(withFilingRollups: false);

        var scored = await _worker.ScoreAllHolders(CancellationToken.None);

        scored.Should().Be(0);
        _fundScoreRepository.GetAll().Should().BeEmpty();
    }

    // A joint filing's accession rolls up under one co-filer only; the other keeps its score fresh
    // through the staleness floor instead of being dropped from discovery.
    [Fact]
    public async Task ScoreAllHolders_AStaleScoreWithoutAFilingRollup_IsRefreshed()
    {
        var holder = SeedDoublingPortfolioAgainstFlatBenchmark(withFilingRollups: false);
        _fundScoreRepository.Add(
            new FundScore
            {
                InstitutionalHolderId = holder.Id,
                BenchmarkTicker = "SPY",
                WindowYears = FundScoringManager.DefaultWindowYears,
                CalculationVersion = FundScore.CurrentCalculationVersion,
                AlphaPercent = -99m,
                CreationTime = DateTime.UtcNow.AddDays(-8),
            }
        );
        await _fundScoreRepository.SaveChanges();

        var scored = await _worker.ScoreAllHolders(CancellationToken.None);

        scored.Should().Be(1);
        var persisted = _fundScoreRepository.GetByHolder(holder).Should().ContainSingle().Subject;
        persisted.AlphaPercent.Should().BeGreaterThan(0m);
    }

    // Every scope shares the one in-memory context so the worker's enumerate scope and per-holder
    // scopes all see the same seeded data and writes.
    [Fact]
    public async Task ScoreAllHolders_AnUnscoreableFilerIsVisitedOnceUntilItsDataChanges()
    {
        var holder = SeedDoublingPortfolioAgainstFlatBenchmark(withPrices: false);

        (await _worker.ScoreAllHolders(CancellationToken.None)).Should().Be(0);

        _fundScoreRepository.GetAll().Should().BeEmpty();
        var attempt = _dbContext.Set<FundScoreAttempt>().Should().ContainSingle().Subject;
        attempt.InstitutionalHolderId.Should().Be(holder.Id);
        attempt.CalculationVersion.Should().Be(FundScore.CurrentCalculationVersion);
        attempt.AttemptedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await _worker.SelectHoldersNeedingScore(CancellationToken.None)).Should().BeEmpty();

        // A rollup imported after the visit makes the filer due again; it is dated before the
        // last filing already on file, so only the import time moves.
        _dbContext
            .Set<InstitutionalFiling>()
            .Add(
                new InstitutionalFiling
                {
                    AccessionNumber = "0001234567-26-999999",
                    InstitutionalHolderId = holder.Id,
                    ReportDate = Today.AddDays(-65),
                    FilingDate = Today.AddDays(-20),
                    PositionCount = 1,
                    TotalValue = 100_000,
                    CreationTime = DateTime.UtcNow.AddMinutes(1),
                }
            );
        await _dbContext.SaveChangesAsync();

        (await _worker.SelectHoldersNeedingScore(CancellationToken.None)).Should().Equal(holder.Id);
    }

    // A visit that faults is left unstamped, so the next cycle retries it.
    [Fact]
    public async Task ScoreAllHolders_AFaultedVisitIsNotStamped()
    {
        var holder = SeedDoublingPortfolioAgainstFlatBenchmark();
        var worker = new FundScoringWorker(
            SharedContextScopeFactory(scoreStoreFaults: true),
            NullLogger<FundScoringWorker>.Instance
        );

        (await worker.ScoreAllHolders(CancellationToken.None)).Should().Be(0);

        _dbContext.Set<FundScoreAttempt>().Should().BeEmpty();
        (await worker.SelectHoldersNeedingScore(CancellationToken.None)).Should().Equal(holder.Id);
    }

    // The benchmark is a cycle-wide precondition: without its prices no filer can score, so the
    // cycle visits nobody rather than stamping the whole universe for the staleness floor.
    [Fact]
    public async Task ScoreAllHolders_WithoutBenchmarkPrices_VisitsNoFiler()
    {
        SeedDoublingPortfolioAgainstFlatBenchmark(withBenchmarkPrices: false);

        (await _worker.ScoreAllHolders(CancellationToken.None)).Should().Be(0);
        _dbContext.Set<FundScoreAttempt>().Should().BeEmpty();

        AddPrice(_benchmark, EarlyPriceDate, 100m);
        AddPrice(_benchmark, Today, 100m);
        await _dbContext.SaveChangesAsync();

        (await _worker.ScoreAllHolders(CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task SelectHoldersNeedingScore_OrdersByTheLastVisitNotTheLastScore()
    {
        var visitedAnHourAgo = SeedFiler("0000000001");
        var visitedTwoDaysAgo = SeedFiler("0000000002");
        // Both scores are past the staleness floor; the older score belongs to the filer visited
        // most recently, so ordering by the score stamp would put it first.
        AddScore(visitedAnHourAgo, scoredAt: DateTime.UtcNow.AddDays(-10));
        AddAttempt(visitedAnHourAgo, attemptedAt: DateTime.UtcNow.AddHours(-1));
        AddScore(visitedTwoDaysAgo, scoredAt: DateTime.UtcNow.AddDays(-9));
        AddAttempt(visitedTwoDaysAgo, attemptedAt: DateTime.UtcNow.AddDays(-2));
        await _dbContext.SaveChangesAsync();

        (await _worker.SelectHoldersNeedingScore(CancellationToken.None))
            .Should()
            .Equal(visitedTwoDaysAgo.Id, visitedAnHourAgo.Id);
    }

    [Fact]
    public async Task SelectHoldersNeedingScore_AVisitUnderAnOlderCalculationBasisIsDueAgain()
    {
        var holder = SeedDoublingPortfolioAgainstFlatBenchmark(withPrices: false);
        _dbContext
            .Set<FundScoreAttempt>()
            .Add(
                new FundScoreAttempt
                {
                    InstitutionalHolderId = holder.Id,
                    BenchmarkTicker = "SPY",
                    WindowYears = FundScoringManager.DefaultWindowYears,
                    CalculationVersion = FundScore.CurrentCalculationVersion - 1,
                    AttemptedAt = DateTime.UtcNow,
                }
            );
        await _dbContext.SaveChangesAsync();

        (await _worker.SelectHoldersNeedingScore(CancellationToken.None)).Should().Equal(holder.Id);
    }

    // scoreStoreFaults makes persisting a score throw, after the backtest ran, so a visit faults
    // inside the scoring call itself.
    private IServiceScopeFactory SharedContextScopeFactory(bool scoreStoreFaults = false)
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var provider = Substitute.For<IServiceProvider>();
                provider.GetService(typeof(EquiblesFinancialDbContext)).Returns(_dbContext);
                provider
                    .GetService(typeof(InstitutionalHolderRepository))
                    .Returns(_ => new InstitutionalHolderRepository(_dbContext));
                provider
                    .GetService(typeof(FundScoringManager))
                    .Returns(_ => new FundScoringManager(
                        new InstitutionalHoldingRepository(_dbContext),
                        new EquityIssuerRepository(_dbContext),
                        new BacktestPriceLoader(
                            new EquityDailyStockPriceRepository(_dbContext),
                            new EquityIssuerRepository(_dbContext),
                            new StockSplitRepository(_dbContext),
                            new BacktestPriceSeriesCache()
                        ),
                        scoreStoreFaults
                            ? FaultingScoreStore()
                            : new FundScoreRepository(_dbContext)
                    ));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return scopeFactory;
    }

    private FundScoreRepository FaultingScoreStore()
    {
        var store = Substitute.ForPartsOf<FundScoreRepository>(_dbContext);
        store
            .When(s => s.SaveChanges())
            .Throw(new InvalidOperationException("score store unavailable"));
        return store;
    }

    private InstitutionalHolder SeedFiler(string cik)
    {
        var holder = new InstitutionalHolder { Cik = cik, Name = $"Filer {cik}" };
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        _dbContext
            .Set<InstitutionalFiling>()
            .Add(
                new InstitutionalFiling
                {
                    AccessionNumber = $"{cik}-26-000001",
                    InstitutionalHolderId = holder.Id,
                    ReportDate = Today.AddDays(-100),
                    FilingDate = Today.AddDays(-30),
                    PositionCount = 1,
                    TotalValue = 100_000,
                    CreationTime = DateTime.UtcNow.AddDays(-30),
                }
            );
        _dbContext.SaveChanges();
        return holder;
    }

    private void AddScore(InstitutionalHolder holder, DateTime scoredAt) =>
        _fundScoreRepository.Add(
            new FundScore
            {
                InstitutionalHolderId = holder.Id,
                BenchmarkTicker = "SPY",
                WindowYears = FundScoringManager.DefaultWindowYears,
                CalculationVersion = FundScore.CurrentCalculationVersion,
                AlphaPercent = 1m,
                CreationTime = scoredAt,
            }
        );

    private void AddAttempt(InstitutionalHolder holder, DateTime attemptedAt) =>
        _dbContext
            .Set<FundScoreAttempt>()
            .Add(
                new FundScoreAttempt
                {
                    InstitutionalHolderId = holder.Id,
                    BenchmarkTicker = "SPY",
                    WindowYears = FundScoringManager.DefaultWindowYears,
                    CalculationVersion = FundScore.CurrentCalculationVersion,
                    AttemptedAt = attemptedAt,
                }
            );

    private InstitutionalHolder SeedDoublingPortfolioAgainstFlatBenchmark(
        bool withFilingRollups = true,
        bool withPrices = true,
        bool withBenchmarkPrices = true
    )
    {
        SeedBenchmark(withBenchmarkPrices);

        EquityIssuer held = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAA",
            Name: "Alpha Co"
        );
        _dbContext.Set<EquityIssuer>().Add(held);
        // Without prices the backtest cannot run, which leaves the filer unscoreable.
        if (withPrices)
        {
            AddPrice(held, EarlyPriceDate, 100m);
            AddPrice(held, Today, 200m);
        }

        var holder = new InstitutionalHolder { Cik = "0001234567", Name = "Doubler Capital" };
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        // The first report's rebalance (ReportDate + 45 days) lands before the window start, so the
        // portfolio is held from day one of the window; the filer then keeps reporting the same
        // position quarterly through today, as a filer that still exists does. The repetition is
        // load-bearing: the simulation stops 150 days after a filer's last rebalance, so a lone
        // filing would be scored as a filer that deregistered and the window would end early.
        for (
            var reportDate = WindowFrom.AddDays(-60);
            reportDate <= Today;
            reportDate = reportDate.AddMonths(3)
        )
        {
            _dbContext
                .Set<InstitutionalHolding>()
                .Add(
                    new InstitutionalHolding
                    {
                        InstitutionalHolderId = holder.Id,
                        EquityIssuerId = held.Id,
                        ReportDate = reportDate,
                        FilingDate = reportDate.AddDays(45),
                        Shares = 1000,
                        Value = 100_000,
                    }
                );
            if (withFilingRollups)
            {
                _dbContext
                    .Set<InstitutionalFiling>()
                    .Add(
                        new InstitutionalFiling
                        {
                            AccessionNumber =
                                $"0001234567-{reportDate:yy}-{reportDate.DayOfYear:D6}",
                            InstitutionalHolderId = holder.Id,
                            ReportDate = reportDate,
                            FilingDate = reportDate.AddDays(45),
                            PositionCount = 1,
                            TotalValue = 100_000,
                        }
                    );
            }
        }

        _dbContext.SaveChanges();
        return holder;
    }

    private void SeedBenchmark(bool withPrices = true)
    {
        _benchmark = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SPY",
            Name: "S&P 500 ETF"
        );
        _dbContext.Set<EquityIssuer>().Add(_benchmark);
        if (withPrices)
        {
            AddPrice(_benchmark, EarlyPriceDate, 100m);
            AddPrice(_benchmark, Today, 100m);
        }
        _dbContext.SaveChanges();
    }

    private void AddPrice(EquityIssuer stock, DateOnly date, decimal close)
    {
        _dbContext
            .Set<EquityDailyStockPrice>()
            .Add(
                new EquityDailyStockPrice
                {
                    Listing = Equibles.TestSupport.NativeListingSeed.ForStock(
                        _dbContext,
                        stock,
                        stock.Presentation.Listing.Ticker
                    ),
                    SourceTicker = stock.Presentation.Listing.Ticker,
                    Date = date,
                    Open = close,
                    High = close,
                    Low = close,
                    Close = close,
                    AdjustedClose = close,
                    Volume = 1_000,
                }
            );
    }
}
