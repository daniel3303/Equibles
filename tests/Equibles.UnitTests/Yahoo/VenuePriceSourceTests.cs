using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Data.Prices;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins the ownership key that keeps venue-derived bars and Yahoo bars apart in one store: the
/// venue key carries a separator no listed ticker can contain, and both query filters are
/// null-safe so a row without a source spelling stays Yahoo's.
/// </summary>
public class VenuePriceSourceTests
{
    [Fact]
    public void Key_IsUpperCaseMicColonIsin()
    {
        VenuePriceSource.Key("xlis", " ptedp0am0009 ").Should().Be("XLIS:PTEDP0AM0009");
        VenuePriceSource.Key("XLIS", "PTEDP0AM0009").Length.Should().BeLessThanOrEqualTo(32);
    }

    [Fact]
    public void Key_RefusesAMissingHalf()
    {
        var noMic = () => VenuePriceSource.Key("", "PTEDP0AM0009");
        var noIsin = () => VenuePriceSource.Key("XLIS", null);
        noMic.Should().Throw<ArgumentException>();
        noIsin.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("XLIS:PTEDP0AM0009", true)]
    [InlineData("AAPL", false)]
    [InlineData("BRK-B", false)]
    [InlineData("BF.B", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsVenueKey_OnlyForTheSeparatorSpelling(string sourceTicker, bool expected)
    {
        VenuePriceSource.IsVenueKey(sourceTicker).Should().Be(expected);
        VenuePriceSource
            .IsVenueOwned(new EquityDailyStockPrice { SourceTicker = sourceTicker })
            .Should()
            .Be(expected);
    }

    [Fact]
    public async Task QueryFilters_SplitTheStoreAndKeepANullSpellingOnYahoosSide()
    {
        await using var db = NewDb();
        var listingId = Guid.NewGuid();
        db.AddRange(
            Row(listingId, new DateOnly(2026, 9, 1), "EDP"),
            Row(listingId, new DateOnly(2026, 9, 2), "XLIS:PTEDP0AM0009"),
            Row(listingId, new DateOnly(2026, 9, 3), null)
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var yahoo = await db.Set<EquityDailyStockPrice>()
            .YahooOwned()
            .Select(p => p.Date)
            .OrderBy(d => d)
            .ToListAsync();
        var venue = await db.Set<EquityDailyStockPrice>()
            .VenueOwned()
            .Select(p => p.Date)
            .ToListAsync();

        yahoo.Should().Equal(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3));
        venue.Should().Equal(new DateOnly(2026, 9, 2));
    }

    private static EquityDailyStockPrice Row(Guid listingId, DateOnly date, string sourceTicker) =>
        new()
        {
            EquityListingId = listingId,
            SourceTicker = sourceTicker,
            Date = date,
            Open = 1,
            High = 1,
            Low = 1,
            Close = 1,
            AdjustedClose = 1,
            Volume = 1,
        };

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new YahooModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }
}
