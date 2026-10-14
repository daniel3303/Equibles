using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// The market-wide boards restate each row from its own issuer's splits: a split attributed to
/// the row's listing after the observation date rescales its share counts, and an unattributed
/// split after that date leaves the basis unknown, so the row is withheld.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsMarketBoardSplitTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly Day = new(2026, 4, 2);
    private static readonly DateOnly Settlement = new(2026, 3, 31);
    private static readonly DateOnly PreviousSettlement = new(2026, 3, 16);

    private int _nextCik = 1;

    public ShortDataToolsMarketBoardSplitTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetLargestShortVolume_RestatesAttributedSplitAndWithholdsUnknownBasis()
    {
        var restated = AddStock("QSA", "Split Co");
        var unknown = AddStock("QSB", "Unknown Basis Co");
        var plain = AddStock("QSC", "Plain Co");
        foreach (var stock in new[] { restated, unknown, plain })
            AddVolume(stock);
        AddSplit(restated, Day.AddDays(10), attributed: true);
        AddSplit(unknown, Day.AddDays(10), attributed: false);
        await DbContext.SaveChangesAsync();
        await using var db = Fixture.CreateDbContext();

        var result = await Sut(db).GetLargestShortVolume();

        result.Should().Contain("| QSA | Split Co | 2,000 | 20 | 8,000 | 25.0% |");
        result.Should().Contain("| QSC | Plain Co | 1,000 | 10 | 4,000 | 25.0% |");
        result.Should().NotContain("QSB");
    }

    [Fact]
    public async Task GetShortInterestSnapshot_RestatesAttributedSplitAndWithholdsUnknownBasis()
    {
        var restated = AddStock("QSA", "Split Co");
        var unknown = AddStock("QSB", "Unknown Basis Co");
        var plain = AddStock("QSC", "Plain Co");
        foreach (var stock in new[] { restated, unknown, plain })
            AddShortInterest(stock);
        AddSplit(restated, Settlement.AddDays(5), attributed: true);
        AddSplit(unknown, Settlement.AddDays(5), attributed: false);
        await DbContext.SaveChangesAsync();
        await using var db = Fixture.CreateDbContext();

        var result = await Sut(db).GetShortInterestSnapshot();

        result.Should().Contain("| QSA | 100,000 | +20,000 | 20,000 | 5.0 |");
        result.Should().Contain("| QSC | 50,000 | +10,000 | 10,000 | 5.0 |");
        result.Should().NotContain("QSB");
    }

    private EquityIssuer AddStock(string ticker, string name)
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: name,
            Cik: (_nextCik++).ToString("D10")
        );
        DbContext.Add(stock);
        return stock;
    }

    private void AddVolume(EquityIssuer stock) =>
        DbContext.Add(
            new DailyShortVolume
            {
                Listing = stock.Presentation.Listing,
                ListedTicker = stock.Presentation.Listing.Ticker,
                Date = Day,
                ShortVolume = 1_000,
                ShortExemptVolume = 10,
                TotalVolume = 4_000,
                Market = "ALL",
            }
        );

    private void AddShortInterest(EquityIssuer stock)
    {
        foreach (
            var (date, position) in new[] { (PreviousSettlement, 40_000), (Settlement, 50_000) }
        )
        {
            DbContext.Add(
                new ShortInterest
                {
                    Listing = stock.Presentation.Listing,
                    ListedTicker = stock.Presentation.Listing.Ticker,
                    SettlementDate = date,
                    CurrentShortPosition = position,
                    PreviousShortPosition = 40_000,
                    ChangeInShortPosition = position - 40_000,
                    AverageDailyVolume = 10_000,
                    DaysToCover = 5m,
                }
            );
        }
    }

    // An unattributed split carries no listing or price-series symbol.
    private void AddSplit(EquityIssuer stock, DateOnly effective, bool attributed) =>
        DbContext.Add(
            new StockSplit
            {
                Issuer = stock,
                Listing = attributed ? stock.Presentation.Listing : null,
                PriceSeriesTicker = attributed ? stock.Presentation.Listing.Ticker : null,
                EffectiveDate = effective,
                Numerator = 2,
                Denominator = 1,
            }
        );

    private ShortDataTools Sut(EquiblesFinancialDbContext db) =>
        new(
            new DailyShortVolumeRepository(db),
            new ShortInterestRepository(db),
            new EquityIssuerRepository(db),
            new ShortSqueezeScoreManager(
                new ShortInterestRepository(db),
                new DailyShortVolumeRepository(db),
                new EquityIssuerRepository(db),
                new StockSplitRepository(db),
                new FailToDeliverRepository(db),
                new EquityDailyStockPriceRepository(db),
                []
            ),
            new StockSplitRepository(db),
            new MemoryCache(new MemoryCacheOptions()),
            estimateSources: [],
            ErrorManager,
            NullLogger<ShortDataTools>()
        );
}
