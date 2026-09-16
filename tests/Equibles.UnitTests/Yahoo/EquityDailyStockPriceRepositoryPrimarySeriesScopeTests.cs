using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.TestSupport;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins the market scope of the issuer-level price reads. Every caller of GetPrimarySeries is a
/// US aggregate (dashboards, screener, index forecasts, insider price backfills), so a verified
/// venue presentation listing must never contribute its EUR series to them; venue pages read by
/// listing id, which stays unscoped.
/// </summary>
public class EquityDailyStockPriceRepositoryPrimarySeriesScopeTests
{
    private static readonly DateOnly Date = new(2026, 9, 15);

    [Fact]
    public async Task GetPrimarySeries_ReturnsOnlyUsPresentationRows()
    {
        await using var db = NewDb();
        var (us, venue) = await Seed(db);
        var repo = new EquityDailyStockPriceRepository(db);

        var primary = await repo.GetPrimarySeries().Select(p => p.EquityListingId).ToListAsync();
        var all = await repo.GetAllSeries().CountAsync();
        var byVenueListing = await repo.GetByListing(venue.Presentation.EquityListingId)
            .CountAsync();
        var venueIssuerLevel = await repo.GetByStock(venue).CountAsync();
        var usIssuerLevel = await repo.GetByStock(us).CountAsync();

        primary.Should().Equal(us.Presentation.EquityListingId);
        all.Should().Be(2);
        byVenueListing.Should().Be(1);
        venueIssuerLevel.Should().Be(0);
        usIssuerLevel.Should().Be(1);
    }

    [Fact]
    public async Task GetLatestDateAcrossAllStocks_IgnoresANewerVenueBar()
    {
        await using var db = NewDb();
        var (_, venue) = await Seed(db);
        db.Add(Row(venue.Presentation.Listing, Date.AddDays(1), "XLIS:PTEDP0AM0009"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repo = new EquityDailyStockPriceRepository(db);

        var latest = await repo.GetLatestDateAcrossAllStocks().ToListAsync();

        latest.Should().Equal(Date);
    }

    private static async Task<(EquityIssuer Us, EquityIssuer Venue)> Seed(
        EquiblesFinancialDbContext db
    )
    {
        EquityIssuer us = EquityIssuerSeed.Create(Ticker: "AAPL", Name: "Apple Inc.");
        EquityIssuer venue = EquityIssuerSeed.Create(
            Ticker: "EDP",
            Name: "EDP - Energias de Portugal, S.A.",
            Isin: "PTEDP0AM0009",
            MarketCountryCode: "PT",
            MarketIdentifierCode: "XLIS",
            IdentityState: EquityIdentityState.Verified,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );
        db.AddRange(us, venue);
        db.AddRange(
            Row(us.Presentation.Listing, Date, "AAPL"),
            Row(venue.Presentation.Listing, Date, "EDP")
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (us, venue);
    }

    private static EquityDailyStockPrice Row(
        EquityListing listing,
        DateOnly date,
        string sourceTicker
    ) =>
        new()
        {
            EquityListingId = listing.Id,
            SourceTicker = sourceTicker,
            Date = date,
            Open = 10,
            High = 10,
            Low = 10,
            Close = 10,
            AdjustedClose = 10,
            Volume = 100,
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
