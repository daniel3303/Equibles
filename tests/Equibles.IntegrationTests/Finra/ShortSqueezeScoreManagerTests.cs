using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.Data;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using FluentAssertions;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

public class ShortSqueezeScoreManagerTests : IDisposable
{
    private static readonly DateOnly SettlementDate = new(2026, 1, 15);

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ShortSqueezeScoreManager _manager;
    private readonly StubEarningsProximitySource _earningsSource = new();
    private readonly List<IEarningsProximitySource> _earningsProximitySources;

    public ShortSqueezeScoreManagerTests()
    {
        _earningsProximitySources = [_earningsSource];
        _dbContext = TestDbContextFactory.Create(
            new FinraModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new FailToDeliverOnlyModule(),
            new YahooModuleConfiguration()
        );
        _manager = new ShortSqueezeScoreManager(
            new ShortInterestRepository(_dbContext),
            new DailyShortVolumeRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            _earningsProximitySources
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
    public async Task Compute_ZeroAverageDailyVolume_DropsDaysToCoverFactor()
    {
        // FINRA publishes days-to-cover as a 1000.0 sentinel when a listing has zero
        // average daily volume — a division-by-zero placeholder, not a measurement.
        // The factor must drop out (like a missing trend) so an untradeable shell
        // can't outrank genuinely squeezed stocks on the sentinel alone.
        var shell = SeedStock("SHEL", sharesOutstanding: 2_000_000);
        var real = SeedStock("REAL", sharesOutstanding: 1_000_000);
        _dbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStockId = shell.Id,
                    SettlementDate = SettlementDate,
                    CurrentShortPosition = 200_000,
                    AverageDailyVolume = 0,
                    DaysToCover = 1000m,
                }
            );
        SeedShortInterest(real, shortPosition: 300_000, daysToCover: 12m);
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        var sentinel = scores.Single(s => s.Ticker == "SHEL");
        sentinel.DaysToCover.Should().BeNull("a zero-volume sentinel is not a measurement");
        sentinel.DaysToCoverPercentile.Should().BeNull();
        // REAL leads on every remaining factor, so it must outrank the shell.
        scores.Select(s => s.Ticker).Should().Equal("REAL", "SHEL");
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

    [Fact]
    public async Task Compute_ImpossibleShortInterestRatio_ExcludesStockFromUniverse()
    {
        // A listing whose issuer's common-stock record cannot back the reported short
        // position — e.g. exchange-traded notes whose issuer's common stock is a nominal
        // one-share count held by its parent — yields a ratio no real stock has ever had.
        // Such a stock must drop out of the universe (the unknown-share-count treatment),
        // not rank as top squeeze risk on a meaningless figure, while an extreme-but-genuine
        // reading (the 2021 GameStop peak was ~1.4x) is still scored.
        var typical = SeedStock("TYP", sharesOutstanding: 1_000_000);
        var extreme = SeedStock("EXTR", sharesOutstanding: 1_000_000);
        var impossible = SeedStock("NOTE", sharesOutstanding: 1);
        SeedShortInterest(typical, shortPosition: 100_000, daysToCover: 2m);
        SeedShortInterest(extreme, shortPosition: 1_400_000, daysToCover: 8m);
        SeedShortInterest(impossible, shortPosition: 15_566, daysToCover: 1m);
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        scores.Select(s => s.Ticker).Should().BeEquivalentTo(["EXTR", "TYP"]);
        scores.Should().OnlyContain(s => s.ShortInterestPercentOfShares <= 2m);
    }

    [Fact]
    public async Task Compute_NonEquityListing_ExcludesStockFromUniverse()
    {
        // A ticker classified from its 12(b) cover-page title as an
        // exchange-traded note (a baby bond) can never be a squeeze candidate —
        // the issuer's common-share record is the wrong denominator for it.
        // Units stay in: MLP common units are genuine operating equity.
        var equity = SeedStock("EQTY", sharesOutstanding: 1_000_000);
        var note = SeedStock(
            "NOTE",
            sharesOutstanding: 1_000_000,
            listedSecurityType: ListedSecurityType.DebtSecurities
        );
        var mlp = SeedStock(
            "MLP",
            sharesOutstanding: 1_000_000,
            listedSecurityType: ListedSecurityType.Units
        );
        SeedShortInterest(equity, shortPosition: 100_000, daysToCover: 2m);
        SeedShortInterest(note, shortPosition: 150_000, daysToCover: 3m);
        SeedShortInterest(mlp, shortPosition: 120_000, daysToCover: 2m);
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        scores.Select(s => s.Ticker).Should().BeEquivalentTo(["EQTY", "MLP"]);
    }

    [Fact]
    public async Task Compute_NoFailsToDeliverFeed_FactorIsNullForEveryone()
    {
        // An empty FTD table means the feed is absent, not that no stock ever fails
        // to deliver — the factor must be unknowable (null) rather than a flattering
        // universe-wide zero.
        var stock = SeedStock("NOFTD", sharesOutstanding: 1_000_000);
        SeedShortInterest(stock, shortPosition: 100_000, daysToCover: 2m);
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        scores.Single().FailsToDeliverPercentOfShares.Should().BeNull();
        scores.Single().FailsToDeliverPercentile.Should().BeNull();
    }

    [Fact]
    public async Task Compute_FailsToDeliverSpike_ScoresWorstDayAndZeroFillsCoveredPeers()
    {
        // FAILS has a 50k worst day on a 1M share count (5%); CLEAN appears nowhere
        // in a live feed, which is a true zero — delivery is fine — not a missing
        // factor. The zero must be scored, keeping CLEAN below FAILS on the factor.
        var fails = SeedStock("FAILS", sharesOutstanding: 1_000_000);
        var clean = SeedStock("CLEAN", sharesOutstanding: 1_000_000);
        SeedShortInterest(fails, shortPosition: 100_000, daysToCover: 2m);
        SeedShortInterest(clean, shortPosition: 100_000, daysToCover: 2m);
        _dbContext
            .Set<FailToDeliver>()
            .AddRange(
                new FailToDeliver
                {
                    CommonStockId = fails.Id,
                    SettlementDate = SettlementDate.AddDays(-3),
                    Quantity = 50_000,
                    Price = 10m,
                },
                new FailToDeliver
                {
                    CommonStockId = fails.Id,
                    SettlementDate = SettlementDate.AddDays(-10),
                    Quantity = 20_000,
                    Price = 10m,
                }
            );
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        var spiking = scores.Single(s => s.Ticker == "FAILS");
        var covered = scores.Single(s => s.Ticker == "CLEAN");
        spiking.FailsToDeliverPercentOfShares.Should().Be(0.05m, "the WORST day counts");
        covered.FailsToDeliverPercentOfShares.Should().Be(0m);
        spiking.FailsToDeliverPercentile.Should().Be(100);
        covered.FailsToDeliverPercentile.Should().Be(0);
    }

    [Fact]
    public async Task Compute_PreviousReportOnFile_YieldsShortInterestChange()
    {
        // BUILD grew its short position 50% versus the previous report; SHRINK cut
        // it in half. The change factor must carry the signed fraction for both.
        var build = SeedStock("BUILD", sharesOutstanding: 1_000_000);
        var shrink = SeedStock("SHRINK", sharesOutstanding: 1_000_000);
        SeedShortInterest(
            build,
            shortPosition: 150_000,
            daysToCover: 2m,
            previousPosition: 100_000
        );
        SeedShortInterest(
            shrink,
            shortPosition: 100_000,
            daysToCover: 2m,
            previousPosition: 200_000
        );
        // The previous settlement date must exist in the table for the change's
        // split basis to be anchored — seed the previous cycle's rows.
        _dbContext
            .Set<ShortInterest>()
            .AddRange(
                new ShortInterest
                {
                    CommonStockId = build.Id,
                    SettlementDate = SettlementDate.AddDays(-14),
                    CurrentShortPosition = 100_000,
                },
                new ShortInterest
                {
                    CommonStockId = shrink.Id,
                    SettlementDate = SettlementDate.AddDays(-14),
                    CurrentShortPosition = 200_000,
                }
            );
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        scores.Single(s => s.Ticker == "BUILD").ShortInterestChangePercent.Should().Be(0.5m);
        scores.Single(s => s.Ticker == "SHRINK").ShortInterestChangePercent.Should().Be(-0.5m);
    }

    [Fact]
    public async Task Compute_NoPreviousPosition_ChangeFactorDropsOut()
    {
        var fresh = SeedStock("FRESH", sharesOutstanding: 1_000_000);
        SeedShortInterest(fresh, shortPosition: 100_000, daysToCover: 2m);
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        scores.Single().ShortInterestChangePercent.Should().BeNull();
        scores.Single().ShortInterestChangePercentile.Should().BeNull();
    }

    [Fact]
    public async Task Compute_RecentPriceHistory_ProducesPriceFactors()
    {
        // Price factors anchor on the CURRENT price tape (squeeze pressure now),
        // not the settlement date — so the seeded series must be recent relative
        // to the wall clock the manager loads against.
        var stock = SeedStock("PRICED", sharesOutstanding: 1_000_000);
        SeedShortInterest(stock, shortPosition: 100_000, daysToCover: 2m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var i = 0; i < 65; i++)
        {
            var close = i == 64 ? 130m : 100m;
            _dbContext
                .Set<DailyStockPrice>()
                .Add(
                    new DailyStockPrice
                    {
                        CommonStockId = stock.Id,
                        Date = today.AddDays(i - 64),
                        Open = close,
                        High = close,
                        Low = close,
                        Close = close,
                        AdjustedClose = close,
                        Volume = 1_000,
                    }
                );
        }

        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        scores.Single().PriceAboveVwap.Should().NotBeNull();
        scores.Single().PriceAboveVwap.Should().BeGreaterThan(0.25m);
    }

    [Fact]
    public async Task Compute_NoPriceHistory_PriceFactorsDropOut()
    {
        var stock = SeedStock("NOPX", sharesOutstanding: 1_000_000);
        SeedShortInterest(stock, shortPosition: 100_000, daysToCover: 2m);
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        scores.Single().PriceAboveVwap.Should().BeNull();
        scores.Single().PriceAboveVwapPercentile.Should().BeNull();
        scores.Single().HasPriceSpikeCatalyst.Should().BeFalse();
        scores.Single().HasVolumeSurgeCatalyst.Should().BeFalse();
        scores.Single().CatalystBoost.Should().Be(0);
    }

    [Fact]
    public async Task Compute_StockInsideTheEarningsWindow_GetsTheEarningsCatalystBoost()
    {
        // A source flags REPORT as near earnings; PEER is not flagged. The boost is
        // additive on top of the weighted base and must mark the flag for consumers.
        var reporting = SeedStock("REPORT", sharesOutstanding: 1_000_000);
        var peer = SeedStock("PEER", sharesOutstanding: 1_000_000);
        SeedShortInterest(reporting, shortPosition: 100_000, daysToCover: 2m);
        SeedShortInterest(peer, shortPosition: 200_000, daysToCover: 4m);
        _earningsSource.NearEarnings.Add(reporting.Id);
        await _dbContext.SaveChangesAsync();

        var scores = await _manager.Compute();

        var flagged = scores.Single(s => s.Ticker == "REPORT");
        var unflagged = scores.Single(s => s.Ticker == "PEER");
        flagged.HasEarningsProximityCatalyst.Should().BeTrue();
        flagged.CatalystBoost.Should().Be(ShortSqueezeScoreManager.EarningsProximityCatalystBoost);
        flagged.Score.Should().Be(flagged.BaseScore + 10);
        unflagged.HasEarningsProximityCatalyst.Should().BeFalse();
        unflagged.CatalystBoost.Should().Be(0);
    }

    // Test double for the optional earnings-calendar seam: reports exactly the ids
    // the test whitelists, intersected with the requested universe like a real
    // implementation would.
    private class StubEarningsProximitySource : IEarningsProximitySource
    {
        public HashSet<Guid> NearEarnings { get; } = [];

        public Task<IReadOnlySet<Guid>> GetStocksNearEarnings(
            IReadOnlyCollection<Guid> stockIds,
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlySet<Guid>>(NearEarnings.Where(stockIds.Contains).ToHashSet());
    }

    // The full Sec module registers pgvector-typed embedding entities the in-memory
    // provider cannot model; the squeeze manager only needs the FailToDeliver table.
    private class FailToDeliverOnlyModule : Equibles.Data.IFinancialModule
    {
        public void ConfigureEntities(Microsoft.EntityFrameworkCore.ModelBuilder builder) =>
            builder.Entity<FailToDeliver>();
    }

    private CommonStock SeedStock(
        string ticker,
        long sharesOutstanding,
        ListedSecurityType listedSecurityType = ListedSecurityType.Unknown
    )
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Corp.",
            Cik = ticker,
            SharesOutStanding = sharesOutstanding,
            ListedSecurityType = listedSecurityType,
        };
        _dbContext.Set<CommonStock>().Add(stock);
        return stock;
    }

    private void SeedShortInterest(
        CommonStock stock,
        long shortPosition,
        decimal daysToCover,
        long previousPosition = 0
    )
    {
        _dbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStockId = stock.Id,
                    SettlementDate = SettlementDate,
                    CurrentShortPosition = shortPosition,
                    PreviousShortPosition = previousPosition,
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
