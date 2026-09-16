using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Yahoo;

/// <summary>The sync stamp lands only on the listing that still carries the fetched ticker on the fetched venue.</summary>
public class EquityListingRepositoryPriceSyncStampTests
{
    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task Stamp_WritesTheMatchingListing_AndNothingElse()
    {
        await using var db = NewDb();
        var issuer = EquityIssuerSeed.Create(
            Ticker: "EDP",
            Isin: "PTEDP0AM0009",
            MarketCountryCode: "PT",
            MarketIdentifierCode: "XLIS",
            IdentityState: EquityIdentityState.Verified,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );
        db.Add(issuer);
        await db.SaveChangesAsync();
        var listing = issuer.Securities.SelectMany(security => security.Listings).Single();
        var repository = new EquityListingRepository(db);
        var at = new DateTime(2026, 9, 16, 4, 0, 0, DateTimeKind.Utc);

        (await repository.StampPriceSyncAttempt(listing.Id, "XPAR", "EDP", at))
            .Should()
            .BeFalse("another venue");
        (await repository.StampPriceSyncAttempt(listing.Id, "XLIS", "EDPR", at))
            .Should()
            .BeFalse("another ticker");
        (await repository.StampPriceSyncAttempt(listing.Id, "XLIS", "EDP", at)).Should().BeTrue();

        db.ChangeTracker.Clear();
        (await db.Set<EquityListing>().SingleAsync(row => row.Id == listing.Id))
            .YahooPriceSyncAttemptedAt.Should()
            .Be(at);
    }
}
