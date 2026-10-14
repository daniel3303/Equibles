using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Contracts;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Contract: a registered <see cref="IDailyBarRelease"/> hold removes the withheld session from
/// every price tool, including the 52-week range and the day-change basis, and appends its note
/// only when the requested range reached that session. With nothing registered the tools read
/// every stored bar.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsDailyBarReleaseTests : ParadeDbMcpTestBase
{
    private const string HoldNote = "_The 2026-07-31 session is released later on this plan._";
    private static readonly DateOnly HeldSession = new(2026, 7, 31);

    public StockPriceToolsDailyBarReleaseTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private StockPriceTools Sut(DailyBarHold hold) =>
        new(
            new EquityDailyStockPriceRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new Equibles.CorporateActions.Repositories.StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>(),
            hold == null ? [] : [new FixedRelease(hold)]
        );

    private static DailyBarHold Hold => new(HeldSession, HoldNote);

    private sealed class FixedRelease(DailyBarHold hold) : IDailyBarRelease
    {
        public Task<DailyBarHold> GetHold(CancellationToken cancellationToken = default) =>
            Task.FromResult(hold);
    }

    private async Task Seed(string ticker)
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: $"{ticker} Inc",
            Cik: ticker.PadLeft(10, '0')
        );
        DbContext.Set<EquityIssuer>().Add(stock);
        await DbContext.SaveChangesAsync();

        // The held session closes at a new high, so a range that leaked it would show 120.00.
        foreach (
            var (date, close) in new[]
            {
                (new DateOnly(2026, 7, 29), 100m),
                (new DateOnly(2026, 7, 30), 105m),
                (HeldSession, 120m),
            }
        )
        {
            DbContext
                .Set<EquityDailyStockPrice>()
                .Add(
                    new EquityDailyStockPrice
                    {
                        Listing = Equibles.TestSupport.NativeListingSeed.ForStock(
                            DbContext,
                            stock,
                            null
                        ),
                        Date = date,
                        Open = close,
                        High = close,
                        Low = close,
                        Close = close,
                        AdjustedClose = close,
                        Volume = 1_000_000,
                    }
                );
        }
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetLatestClosingPrices_HeldSession_ShowsThePriorSessionWithTheNote()
    {
        await Seed("HLD");

        var result = await Sut(Hold).GetLatestClosingPrices("HLD");

        result
            .Should()
            .Contain("| HLD | 2026-07-30 | 105.00 | +5.00 | +5.00% | 1,000,000 | 105.00");
        result.Should().NotContain("2026-07-31 |").And.NotContain("120.00");
        result.TrimEnd().Should().EndWith(HoldNote);
    }

    [Fact]
    public async Task GetLatestClosingPrices_NoReleaseRegistered_ShowsTheNewestSession()
    {
        await Seed("OPN");

        var result = await Sut(null).GetLatestClosingPrices("OPN");

        result.Should().Contain("| OPN | 2026-07-31 | 120.00 |");
        result.Should().NotContain("released later");
    }

    [Fact]
    public async Task GetStockPrices_RangeReachingTheHeldSession_EndsBeforeItWithTheNote()
    {
        await Seed("RNG");

        var result = await Sut(Hold).GetStockPrices("RNG", "2026-07-01", "2026-08-31");

        result.Should().Contain("| 2026-07-30 |").And.NotContain("| 2026-07-31 |");
        result.TrimEnd().Should().EndWith(HoldNote);
    }

    [Fact]
    public async Task GetStockPrices_RangeEndingBeforeTheHeldSession_CarriesNoNote()
    {
        await Seed("OLD");

        var result = await Sut(Hold).GetStockPrices("OLD", "2026-07-01", "2026-07-30");

        result.Should().Contain("| 2026-07-30 |");
        result.Should().NotContain("released later");
    }

    [Fact]
    public async Task GetStockPrices_OnlyTheHeldSession_SaysNoDataWithTheNote()
    {
        await Seed("DAY");

        var result = await Sut(Hold).GetStockPrices("DAY", "2026-07-31", "2026-07-31");

        result.Should().StartWith("No price data found for DAY");
        result.TrimEnd().Should().EndWith(HoldNote);
    }

    [Fact]
    public async Task TechnicalIndicators_RangeReachingTheHeldSession_EndBeforeItWithTheNote()
    {
        await Seed("TEC");
        var sut = Sut(Hold);

        var outputs = new[]
        {
            await sut.GetOnBalanceVolume("TEC", "2026-07-01", "2026-08-31"),
            await sut.GetAverageTrueRange("TEC", "2026-07-01", "2026-08-31", period: 2),
            await sut.GetBollingerBands("TEC", "2026-07-01", "2026-08-31", period: 2),
            await sut.GetStochasticOscillator("TEC", "2026-07-01", "2026-08-31", kPeriod: 2),
        };

        foreach (var output in outputs)
        {
            output.Should().Contain("| 2026-07-30 |").And.NotContain("| 2026-07-31 |");
            output.TrimEnd().Should().EndWith(HoldNote);
        }
    }
}
