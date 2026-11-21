using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Mcp;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Data.Prices;
using Equibles.Yahoo.Mcp.Captures;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;

namespace Equibles.IntegrationTests.Mcp;

// The price tools record the rows they print, so a host can draw them beside the text.
[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsCaptureTests : ParadeDbMcpTestBase
{
    public StockPriceToolsCaptureTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private StockPriceTools Sut() =>
        new(
            new EquityDailyStockPriceRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new Equibles.CorporateActions.Repositories.StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    private async Task<EquityIssuer> SeedAapl(int days, DateOnly first)
    {
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple Inc",
            Cik: "0000320193"
        );
        DbContext.Set<EquityIssuer>().Add(stock);
        var listing = Equibles.TestSupport.NativeListingSeed.ForStock(DbContext, stock, null);
        DbContext
            .Set<EquityDailyStockPrice>()
            .AddRange(
                Enumerable
                    .Range(0, days)
                    .Select(i => new EquityDailyStockPrice
                    {
                        Listing = listing,
                        EquityListingId = listing.Id,
                        Date = first.AddDays(i),
                        Open = 100m + i,
                        High = 102m + i,
                        Low = 99m + i,
                        Close = 101m + i,
                        AdjustedClose = 101m + i,
                        Volume = 1_000_000 + i,
                    })
            );
        await DbContext.SaveChangesAsync();
        return stock;
    }

    [Fact]
    public async Task GetStockPrices_RecordsTheNewestPrintedBarsOldestFirst()
    {
        await SeedAapl(5, new DateOnly(2026, 4, 1));
        using var scope = McpResultCapture.Begin();

        var text = await Sut()
            .GetStockPrices("AAPL", startDate: "2026-04-01", endDate: "2026-04-30", maxResults: 2);

        var capture = scope.Get<StockPriceHistoryCapture>();
        capture.Should().NotBeNull();
        capture.Interval.Should().Be(PriceBarInterval.Daily);
        capture.TotalInRange.Should().Be(5);
        capture
            .Bars.Select(bar => bar.Date)
            .Should()
            .Equal(new DateOnly(2026, 4, 4), new DateOnly(2026, 4, 5));
        capture.Bars[^1].Close.Should().Be(105m);
        capture.Listing.Symbol.Should().Be("AAPL");
        capture.Listing.Name.Should().Be("Apple Inc");
        capture.Listing.QuoteUnit.Should().BeNull();
        text.Should().Contain("| 2026-04-05 | 104.00 | 106.00 | 103.00 | 105.00 |");
    }

    [Fact]
    public async Task GetStockPrices_Weekly_RecordsTheRolledUpBars()
    {
        await SeedAapl(10, new DateOnly(2026, 4, 1));
        using var scope = McpResultCapture.Begin();

        await Sut()
            .GetStockPrices(
                "AAPL",
                startDate: "2026-04-01",
                endDate: "2026-04-10",
                interval: "weekly"
            );

        var capture = scope.Get<StockPriceHistoryCapture>();
        capture.Interval.Should().Be(PriceBarInterval.Weekly);
        capture.Bars.Should().HaveCount(capture.TotalInRange);
        // 2026-04-01 is a Wednesday: its ISO week starts 03-30, which the range cuts, so the bar is partial.
        capture.Bars[0].PeriodStart.Should().Be(new DateOnly(2026, 3, 30));
        capture.Bars[0].Partial.Should().BeTrue();
        capture.Bars[0].Open.Should().Be(100m);
    }

    [Fact]
    public async Task GetStockPrices_UnknownTicker_RecordsNothing()
    {
        using var scope = McpResultCapture.Begin();

        await Sut().GetStockPrices("ZZZZ");

        scope.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBollingerBands_RecordsOnlyThePrintedRowsWithTheirBandValues()
    {
        await SeedAapl(40, new DateOnly(2026, 3, 1));
        using var scope = McpResultCapture.Begin();

        var text = await Sut()
            .GetBollingerBands(
                "AAPL",
                startDate: "2026-03-01",
                endDate: "2026-04-30",
                period: 5,
                maxResults: 3
            );

        var capture = scope.Get<StockIndicatorCapture>();
        capture.Kind.Should().Be(StockIndicatorKind.BollingerBands);
        capture.Lines.Should().Equal("Lower", "Middle", "Upper");
        capture
            .Bars.Select(bar => bar.Date)
            .Should()
            .Equal(new DateOnly(2026, 4, 7), new DateOnly(2026, 4, 8), new DateOnly(2026, 4, 9));
        capture.Values.Should().AllSatisfy(line => line.Should().HaveCount(3));
        // The middle band of the newest row is the mean of its last five closes (136..140), as the table prints it.
        capture.Values[1][^1].Should().Be(138m);
        text.Should().Contain("| 2026-04-09 | 140.00 |").And.Contain("| 138.00 |");
        capture.TotalInRange.Should().Be(40);
        // A 5-bar band reads four bars before the first printed row (seeded one a day, so 04-03 to 04-06).
        capture.ReadsBack.Should().Be(5);
        capture
            .WarmupDates.Should()
            .Equal(
                new DateOnly(2026, 4, 3),
                new DateOnly(2026, 4, 4),
                new DateOnly(2026, 4, 5),
                new DateOnly(2026, 4, 6)
            );
    }

    [Fact]
    public async Task GetLatestClosingPrices_RecordsDataRowsAndPlaceholdersInTableOrder()
    {
        await SeedAapl(3, new DateOnly(2026, 4, 7));
        using var scope = McpResultCapture.Begin();

        await Sut().GetLatestClosingPrices(["AAPL", "ZZZZ", "^GSPC"]);

        var capture = scope.Get<LatestClosingPricesCapture>();
        capture.Rows.Select(row => row.Symbol).Should().Equal("AAPL", "ZZZZ", "^GSPC");
        var apple = capture.Rows[0];
        apple.Close.Should().Be(103m);
        apple.Change.Should().Be(1m);
        apple.Date.Should().Be(new DateOnly(2026, 4, 9));
        apple.High52Weeks.Should().Be(103m);
        apple.Low52Weeks.Should().Be(101m);
        apple.RangeStarred.Should().BeTrue();
        capture.Rows[1].Status.Should().Be("Not found");
        capture.Rows[2].Status.Should().Be("Invalid ticker");
        capture.Rows[1].Listing.Should().BeNull();
    }
}
