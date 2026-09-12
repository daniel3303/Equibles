using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.TestSupport;

// Explicit identity setup for fixtures that still exercise the legacy stock-facing boundary.
internal static class NativeListingSeed
{
    public static EquityListing ForStockId(DbContext db, Guid stockId) =>
        ForStock(
            db,
            db.Set<CommonStock>().Local.FirstOrDefault(row => row.Id == stockId)
                ?? db.Set<CommonStock>().Single(row => row.Id == stockId)
        );

    public static EquityListing ForStock(DbContext db, CommonStock stock)
    {
        if (db.Database.IsRelational())
        {
            var stored = db.Set<LegacyEquityListing>()
                .Where(row => row.CommonStockId == stock.Id && row.ListedTicker == stock.Ticker)
                .Select(row => row.Listing)
                .SingleOrDefault();
            if (stored != null)
                return stored;
            if (db.Entry(stock).State == EntityState.Detached)
                db.Add(stock);
            if (db.Entry(stock).State == EntityState.Added)
                db.SaveChanges();
            return db.Set<LegacyEquityListing>()
                .Where(row => row.CommonStockId == stock.Id && row.ListedTicker == stock.Ticker)
                .Select(row => row.Listing)
                .Single();
        }

        var issuer =
            db.Set<EquityIssuer>().Local.FirstOrDefault(row => row.Id == stock.Id)
            ?? db.Set<EquityIssuer>().Find(stock.Id)
            ?? new EquityIssuer { Id = stock.Id, Name = stock.Name };
        var listing = issuer.Presentation?.Listing ?? new EquityListing { Ticker = stock.Ticker };
        listing.Security ??= new EquitySecurity { Issuer = issuer, EquityIssuerId = issuer.Id };
        issuer.Presentation ??= new EquityIssuerPresentation { Issuer = issuer, Listing = listing };
        if (db.Entry(issuer).State == EntityState.Detached)
            db.Add(issuer);
        if (db.Entry(listing).State == EntityState.Detached)
            db.Add(listing);
        if (
            !db.Set<LegacyEquityListing>().Local.Any(row => row.EquityListingId == listing.Id)
            && !db.Set<LegacyEquityListing>().Any(row => row.EquityListingId == listing.Id)
        )
            db.Add(
                new LegacyEquityListing
                {
                    CommonStockId = stock.Id,
                    ListedTicker = stock.Ticker,
                    Listing = listing,
                }
            );
        return listing;
    }
}
