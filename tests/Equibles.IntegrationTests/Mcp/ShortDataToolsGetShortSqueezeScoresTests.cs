using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using FluentAssertions;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsGetShortSqueezeScoresTests : ParadeDbMcpTestBase
{
    private ShortDataTools Sut() =>
        new(
            new DailyShortVolumeRepository(DbContext),
            new ShortInterestRepository(DbContext),
            new CommonStockRepository(DbContext),
            new ShortSqueezeScoreManager(
                new ShortInterestRepository(DbContext),
                new DailyShortVolumeRepository(DbContext),
                new CommonStockRepository(DbContext),
                new StockSplitRepository(DbContext),
                new FailToDeliverRepository(DbContext),
                new DailyStockPriceRepository(DbContext),
                []
            ),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<ShortDataTools>()
        );

    public ShortDataToolsGetShortSqueezeScoresTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // The tool renders the scored universe highest-composite first, anchored to the
    // latest settlement date, and says so plainly when nothing is scored.
    [Fact]
    public async Task GetShortSqueezeScores_RanksScoredStocksHighestFirst()
    {
        var settlement = new DateOnly(2026, 4, 15);
        var hot = new CommonStock
        {
            Ticker = "HOT",
            Name = "Hot Corp",
            Cik = "0000000101",
            SharesOutStanding = 1_000_000,
        };
        var cold = new CommonStock
        {
            Ticker = "COLD",
            Name = "Cold Corp",
            Cik = "0000000102",
            SharesOutStanding = 1_000_000,
        };
        DbContext.Set<CommonStock>().AddRange(hot, cold);
        DbContext
            .Set<ShortInterest>()
            .AddRange(
                new ShortInterest
                {
                    CommonStockId = hot.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 300_000,
                    DaysToCover = 8m,
                },
                new ShortInterest
                {
                    CommonStockId = cold.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 50_000,
                    DaysToCover = 1m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortSqueezeScores();

        result.Should().Contain("settlement 2026-04-15");
        result
            .IndexOf("| 1 | HOT |")
            .Should()
            .BeGreaterThan(0, "the harder-shorted stock ranks first");
        result.IndexOf("| 2 | COLD |").Should().BeGreaterThan(result.IndexOf("| 1 | HOT |"));
        result.Should().Contain("30.0");
    }

    [Fact]
    public async Task GetShortSqueezeScores_NoData_SaysSo()
    {
        var result = await Sut().GetShortSqueezeScores();

        result.Should().Contain("No short-squeeze scores available");
    }

    // The raw board is dominated by untradeable micro-caps; the liquidity gates must
    // drop them from the view (nulls fail an active gate) while leaving the scored
    // universe — and therefore every score — untouched.
    [Fact]
    public async Task GetShortSqueezeScores_MinMarketCap_DropsMicroCapsAndUnknowns()
    {
        var settlement = new DateOnly(2026, 4, 15);
        var large = new CommonStock
        {
            Ticker = "BIG",
            Name = "Big Corp",
            Cik = "0000000201",
            SharesOutStanding = 100_000_000,
            MarketCapitalization = 5_000_000_000,
        };
        var micro = new CommonStock
        {
            Ticker = "TINY",
            Name = "Tiny Bio",
            Cik = "0000000202",
            SharesOutStanding = 1_000_000,
            MarketCapitalization = 50_000_000,
        };
        var unknown = new CommonStock
        {
            Ticker = "NOCAP",
            Name = "No Cap Corp",
            Cik = "0000000203",
            SharesOutStanding = 1_000_000,
        };
        DbContext.Set<CommonStock>().AddRange(large, micro, unknown);
        DbContext
            .Set<ShortInterest>()
            .AddRange(
                new ShortInterest
                {
                    CommonStockId = large.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 10_000_000,
                    AverageDailyVolume = 2_000_000,
                    DaysToCover = 5m,
                },
                new ShortInterest
                {
                    CommonStockId = micro.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 400_000,
                    DaysToCover = 20m,
                },
                new ShortInterest
                {
                    CommonStockId = unknown.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 300_000,
                    DaysToCover = 15m,
                }
            );
        await DbContext.SaveChangesAsync();

        var unfiltered = await Sut().GetShortSqueezeScores();
        var filtered = await Sut().GetShortSqueezeScores(minMarketCap: 300_000_000);

        unfiltered.Should().Contain("TINY").And.Contain("NOCAP").And.Contain("BIG");
        filtered.Should().Contain("BIG");
        filtered.Should().NotContain("TINY", "a $50M cap fails the $300M floor");
        filtered.Should().NotContain("NOCAP", "an unknown market cap fails an active gate");

        // Liquidity context renders: $5B cap and ~$100M/day (2M shares × $50 implied).
        filtered.Should().Contain("$5B");
        filtered.Should().Contain("$100M");
    }

    [Fact]
    public async Task GetShortSqueezeScores_MinDollarVolume_NothingClears_ExplainsInsteadOfEmptyTable()
    {
        var settlement = new DateOnly(2026, 4, 15);
        var stock = new CommonStock
        {
            Ticker = "THIN",
            Name = "Thinly Traded Corp",
            Cik = "0000000204",
            SharesOutStanding = 1_000_000,
            MarketCapitalization = 10_000_000,
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStockId = stock.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 100_000,
                    AverageDailyVolume = 10_000,
                    DaysToCover = 10m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortSqueezeScores(minDollarVolume: 5_000_000);

        result.Should().Contain("No scored stocks clear the requested liquidity floor");
    }

    // The single-ticker lookup answers "does MY stock look squeeze-prone": the score,
    // its rank within the scored universe, and the factor breakdown — data the board
    // only exposes for the top of the ranking.
    [Fact]
    public async Task GetShortSqueezeScores_Ticker_RendersScoreCardWithUniverseRank()
    {
        var settlement = new DateOnly(2026, 4, 15);
        var hot = new CommonStock
        {
            Ticker = "HOT",
            Name = "Hot Corp",
            Cik = "0000000301",
            SharesOutStanding = 1_000_000,
        };
        var cold = new CommonStock
        {
            Ticker = "COLD",
            Name = "Cold Corp",
            Cik = "0000000302",
            SharesOutStanding = 1_000_000,
        };
        DbContext.Set<CommonStock>().AddRange(hot, cold);
        DbContext
            .Set<ShortInterest>()
            .AddRange(
                new ShortInterest
                {
                    CommonStockId = hot.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 300_000,
                    DaysToCover = 8m,
                },
                new ShortInterest
                {
                    CommonStockId = cold.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 50_000,
                    DaysToCover = 1m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortSqueezeScores(ticker: "cold");

        result.Should().Contain("Short-squeeze score — COLD (Cold Corp)");
        result.Should().Contain("rank 2 of 2 scored stocks");
        // Invariant "P1" renders a space before the percent sign.
        result.Should().Contain("Short interest: 5.0 % of shares outstanding");
        result.Should().Contain("settlement 2026-04-15");
    }

    [Fact]
    public async Task GetShortSqueezeScores_TickerNotScored_ExplainsWhy()
    {
        var settlement = new DateOnly(2026, 4, 15);
        var scored = new CommonStock
        {
            Ticker = "HOT",
            Name = "Hot Corp",
            Cik = "0000000303",
            SharesOutStanding = 1_000_000,
        };
        var unscored = new CommonStock
        {
            Ticker = "QUIET",
            Name = "Quiet Corp",
            Cik = "0000000304",
            SharesOutStanding = 1_000_000,
        };
        DbContext.Set<CommonStock>().AddRange(scored, unscored);
        DbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStockId = scored.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 300_000,
                    DaysToCover = 8m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortSqueezeScores(ticker: "QUIET");

        result.Should().Contain("QUIET is not in the scored universe at settlement 2026-04-15");
    }

    // A SIC-6221 commodity/currency trust registers "units" on its 12(b) cover exactly
    // like an MLP, but its creatable units cannot be squeezed — the authoritative
    // (Units, SIC 6221) pair must keep it off the board while the MLP stays ranked.
    [Fact]
    public async Task GetShortSqueezeScores_CommodityTrustUnits_ExcludedWhileMlpUnitsRank()
    {
        var settlement = new DateOnly(2026, 4, 15);
        var mlp = new CommonStock
        {
            Ticker = "MLP",
            Name = "Pipeline Partners LP",
            Cik = "0000000305",
            SharesOutStanding = 1_000_000,
            ListedSecurityType = ListedSecurityType.Units,
            Sic = "4922",
        };
        var trust = new CommonStock
        {
            Ticker = "FXZ",
            Name = "CurrencyShares Test Trust",
            Cik = "0000000306",
            SharesOutStanding = 1_000_000,
            ListedSecurityType = ListedSecurityType.Units,
            Sic = "6221",
        };
        DbContext.Set<CommonStock>().AddRange(mlp, trust);
        DbContext
            .Set<ShortInterest>()
            .AddRange(
                new ShortInterest
                {
                    CommonStockId = mlp.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 200_000,
                    DaysToCover = 5m,
                },
                new ShortInterest
                {
                    CommonStockId = trust.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 300_000,
                    DaysToCover = 9m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortSqueezeScores();

        result.Should().Contain("MLP", "MLP common units are genuine operating equity");
        result.Should().NotContain("FXZ", "trust units are created/redeemed at NAV — no squeeze");
    }
}
