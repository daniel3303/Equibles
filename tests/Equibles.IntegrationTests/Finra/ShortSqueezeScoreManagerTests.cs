using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.Data;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

public class ShortSqueezeScoreManagerTests : IDisposable
{
    private static readonly DateOnly SettlementDate = new(2026, 1, 15);

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ShortSqueezeScoreManager _manager;

    public ShortSqueezeScoreManagerTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new FinraModuleConfiguration(),
            new CommonStocksModuleConfiguration()
        );
        _manager = new ShortSqueezeScoreManager(
            new ShortInterestRepository(_dbContext),
            new DailyShortVolumeRepository(_dbContext),
            new CommonStockRepository(_dbContext)
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Compute_RanksUniverseByCompositePercentiles()
    {
        // Three stocks ordered the same way on every factor: HOT shorted hardest with
        // the slowest cover and a rising short-volume share, COLD the opposite, MID
        // between — so the composite must rank HOT > MID > COLD with the extremes at
        // the percentile bounds.
        var hot = SeedStock("HOT", sharesOutstanding: 1_000_000);
        var mid = SeedStock("MID", sharesOutstanding: 1_000_000);
        var cold = SeedStock("COLD", sharesOutstanding: 1_000_000);
        SeedShortInterest(hot, shortPosition: 300_000, daysToCover: 8m);
        SeedShortInterest(mid, shortPosition: 150_000, daysToCover: 4m);
        SeedShortInterest(cold, shortPosition: 50_000, daysToCover: 1m);
        // Short-volume share moves +20pp for HOT, stays flat for MID, −20pp for COLD.
        SeedShortVolume(hot, priorShort: 300, recentShort: 500);
        SeedShortVolume(mid, priorShort: 400, recentShort: 400);
        SeedShortVolume(cold, priorShort: 500, recentShort: 300);
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        scores.Select(s => s.Ticker).Should().Equal("HOT", "MID", "COLD");
        scores[0].Score.Should().Be(100);
        scores[1].Score.Should().Be(50);
        scores[2].Score.Should().Be(0);
        scores[0].SettlementDate.Should().Be(SettlementDate);
        scores[0].ShortInterestPercentOfShares.Should().Be(0.3m);
        scores[0].DaysToCover.Should().Be(8m);
        scores[0].ShortVolumeShareTrend.Should().Be(0.2m);
        scores[0].DaysToCoverPercentile.Should().Be(100);
        scores[0].ShortVolumeTrendPercentile.Should().Be(100);
    }

    [Fact]
    public async Task Compute_StockWithoutVolumeRows_DropsTrendFactorFromItsMean()
    {
        // NOVOL has no daily short-volume rows, so its trend factor must be null and
        // its composite the mean of the two remaining percentiles — not a defaulted
        // zero that would sink it below peers with identical short interest.
        var withVolume = SeedStock("HASVOL", sharesOutstanding: 1_000_000);
        var withoutVolume = SeedStock("NOVOL", sharesOutstanding: 1_000_000);
        SeedShortInterest(withVolume, shortPosition: 100_000, daysToCover: 2m);
        SeedShortInterest(withoutVolume, shortPosition: 200_000, daysToCover: 5m);
        SeedShortVolume(withVolume, priorShort: 300, recentShort: 500);
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        var noVolume = scores.Single(s => s.Ticker == "NOVOL");
        noVolume.ShortVolumeShareTrend.Should().BeNull();
        noVolume.ShortVolumeTrendPercentile.Should().BeNull();
        // Both of NOVOL's available factors sit at the top of a two-stock universe.
        noVolume.Score.Should().Be(100);
    }

    [Fact]
    public async Task Compute_ReportedDaysToCoverMissing_ComputedFromAverageDailyVolume()
    {
        var stock = SeedStock("CALC", sharesOutstanding: 1_000_000);
        _dbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStockId = stock.Id,
                    SettlementDate = SettlementDate,
                    CurrentShortPosition = 120_000,
                    AverageDailyVolume = 40_000,
                    DaysToCover = null,
                }
            );
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        scores.Single().DaysToCover.Should().Be(3m);
    }

    [Fact]
    public async Task Compute_NoShortInterestData_ReturnsEmpty()
    {
        var scores = await _manager.Compute();

        scores.Should().BeEmpty();
    }

    [Fact]
    public async Task Compute_UnknownSharesOutstanding_ExcludesStockFromUniverse()
    {
        // SharesOutStanding == 0 means "unknown" across the codebase — a percent of an
        // unknown denominator is meaningless, so the stock must not be scored at all.
        var known = SeedStock("KNOWN", sharesOutstanding: 1_000_000);
        var unknown = SeedStock("UNKNOWN", sharesOutstanding: 0);
        SeedShortInterest(known, shortPosition: 100_000, daysToCover: 2m);
        SeedShortInterest(unknown, shortPosition: 900_000, daysToCover: 9m);
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        scores.Select(s => s.Ticker).Should().Equal("KNOWN");
    }

    private CommonStock SeedStock(string ticker, long sharesOutstanding)
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Corp.",
            Cik = ticker,
            SharesOutStanding = sharesOutstanding,
        };
        _dbContext.Set<CommonStock>().Add(stock);
        return stock;
    }

    private void SeedShortInterest(CommonStock stock, long shortPosition, decimal daysToCover)
    {
        _dbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStockId = stock.Id,
                    SettlementDate = SettlementDate,
                    CurrentShortPosition = shortPosition,
                    DaysToCover = daysToCover,
                }
            );
    }

    // One row in the prior window and one in the recent window, constant total volume,
    // so the pooled short-volume share trend is exactly (recentShort - priorShort) / 1000.
    private void SeedShortVolume(CommonStock stock, long priorShort, long recentShort)
    {
        _dbContext
            .Set<DailyShortVolume>()
            .AddRange(
                new DailyShortVolume
                {
                    CommonStockId = stock.Id,
                    Date = SettlementDate.AddDays(-20),
                    ShortVolume = priorShort,
                    TotalVolume = 1000,
                    Market = "Q",
                },
                new DailyShortVolume
                {
                    CommonStockId = stock.Id,
                    Date = SettlementDate.AddDays(-5),
                    ShortVolume = recentShort,
                    TotalVolume = 1000,
                    Market = "Q",
                }
            );
    }
}
