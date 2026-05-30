using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
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
/// Contract: <see cref="FundScoringWorker"/> enumerates every filer that has 13F holdings,
/// scores each in its own scope, and persists the results.
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

    public FundScoringWorkerTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new HoldingsModuleConfiguration(),
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
    }

    [Fact]
    public async Task ScoreAllHolders_NoHoldings_ScoresNothing()
    {
        SeedBenchmark();

        var scored = await _worker.ScoreAllHolders(CancellationToken.None);

        scored.Should().Be(0);
        _fundScoreRepository.GetAll().Should().BeEmpty();
    }

    // Every scope shares the one in-memory context so the worker's enumerate scope and per-holder
    // scopes all see the same seeded data and writes.
    private IServiceScopeFactory SharedContextScopeFactory()
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
                        new CommonStockRepository(_dbContext),
                        new DailyStockPriceRepository(_dbContext),
                        new FundScoreRepository(_dbContext)
                    ));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return scopeFactory;
    }

    private InstitutionalHolder SeedDoublingPortfolioAgainstFlatBenchmark()
    {
        SeedBenchmark();

        var held = new CommonStock { Ticker = "AAA", Name = "Alpha Co" };
        _dbContext.Set<CommonStock>().Add(held);
        AddPrice(held, EarlyPriceDate, 100m);
        AddPrice(held, Today, 200m);

        var holder = new InstitutionalHolder { Cik = "0001234567", Name = "Doubler Capital" };
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        // Rebalance (ReportDate + 45 days) lands before the window start, so the portfolio is
        // held from day one of the window through to today.
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(
                new InstitutionalHolding
                {
                    InstitutionalHolderId = holder.Id,
                    CommonStockId = held.Id,
                    ReportDate = WindowFrom.AddDays(-60),
                    FilingDate = WindowFrom.AddDays(-15),
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
        AddPrice(benchmark, EarlyPriceDate, 100m);
        AddPrice(benchmark, Today, 100m);
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
