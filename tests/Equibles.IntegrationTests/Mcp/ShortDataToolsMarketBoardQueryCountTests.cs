using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// The market-wide boards read a whole day, so a navigation the queries leave unloaded becomes
/// one lazy-load round trip per row. Each board must issue a fixed number of commands however
/// many rows and attributed splits the day holds.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsMarketBoardQueryCountTests : ParadeDbMcpTestBase
{
    private const int Stocks = 24;
    private const int MaxCommands = 12;
    private static readonly DateOnly Day = new(2026, 4, 2);
    private static readonly DateOnly Settlement = new(2026, 3, 31);

    public ShortDataToolsMarketBoardQueryCountTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetLargestShortVolume_CommandCountDoesNotGrowWithRows()
    {
        var tickers = await Seed();
        var counter = new CommandCounter();
        await using var db = Fixture.CreateDbContext(options => options.AddInterceptors(counter));

        var result = await Sut(db).GetLargestShortVolume();

        foreach (var ticker in tickers)
            result.Should().Contain($"| {ticker} |");
        counter.Commands.Should().BeLessThanOrEqualTo(MaxCommands);
    }

    [Fact]
    public async Task GetShortInterestSnapshot_CommandCountDoesNotGrowWithRows()
    {
        var tickers = await Seed();
        var counter = new CommandCounter();
        await using var db = Fixture.CreateDbContext(options => options.AddInterceptors(counter));

        var result = await Sut(db).GetShortInterestSnapshot();

        foreach (var ticker in tickers)
            result.Should().Contain($"| {ticker} |");
        counter.Commands.Should().BeLessThanOrEqualTo(MaxCommands);
    }

    // Every stock carries a split attributed to a second listing that no board row loads.
    private async Task<List<string>> Seed()
    {
        var tickers = new List<string>();
        for (var index = 0; index < Stocks; index++)
        {
            var ticker = $"QC{(char)('A' + index)}";
            tickers.Add(ticker);
            EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
                Ticker: ticker,
                Name: $"Query Count {index}",
                Cik: (index + 1).ToString("D10")
            );
            DbContext.Add(stock);
            var listing = stock.Presentation.Listing;
            var sibling = Equibles.TestSupport.NativeListingSeed.ForStock(
                DbContext,
                stock,
                $"{ticker}W"
            );
            DbContext.AddRange(
                new DailyShortVolume
                {
                    Listing = listing,
                    ListedTicker = ticker,
                    Date = Day,
                    ShortVolume = 1_000 + index,
                    TotalVolume = 10_000,
                    Market = "ALL",
                },
                new ShortInterest
                {
                    Listing = listing,
                    ListedTicker = ticker,
                    SettlementDate = Settlement,
                    CurrentShortPosition = 50_000 + index,
                    PreviousShortPosition = 40_000,
                    AverageDailyVolume = 10_000,
                    DaysToCover = 5m,
                },
                new ShortInterest
                {
                    Listing = listing,
                    ListedTicker = ticker,
                    SettlementDate = Settlement.AddDays(-15),
                    CurrentShortPosition = 40_000,
                    AverageDailyVolume = 10_000,
                    DaysToCover = 4m,
                },
                new StockSplit
                {
                    Issuer = stock,
                    Listing = sibling,
                    PriceSeriesTicker = sibling.Ticker,
                    EffectiveDate = Day.AddDays(-100),
                    Numerator = 2,
                    Denominator = 1,
                }
            );
        }
        await DbContext.SaveChangesAsync();
        return tickers;
    }

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

    // Lazy loads run synchronously, so both reader paths are counted.
    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Commands { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Commands++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands++;
            return ValueTask.FromResult(result);
        }
    }
}
