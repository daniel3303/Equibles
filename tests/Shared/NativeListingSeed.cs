using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.TestSupport;

// Explicit identity setup for fixtures that still exercise the legacy stock-facing boundary.
internal static class NativeListingSeed
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        CommonStock,
        Dictionary<string, EquityListing>
    > Detached = new();

    public static EquityListing ForStockId(DbContext db, Guid issuerId, string listedTicker = null)
    {
        var issuer =
            db.Set<EquityIssuer>().Local.FirstOrDefault(row => row.Id == issuerId)
            ?? db.Set<EquityIssuer>()
                .Include(row => row.Presentation)
                    .ThenInclude(row => row.Listing)
                .Include(row => row.Securities)
                    .ThenInclude(row => row.Listings)
                .SingleOrDefault(row => row.Id == issuerId);
        return issuer != null
            ? ForStock(db, issuer, listedTicker)
            : ForStock(db, db.Set<CommonStock>().Single(row => row.Id == issuerId), listedTicker);
    }

    public static EquityListing ForStock(
        DbContext db,
        EquityIssuer issuer,
        string listedTicker = null
    )
    {
        var ticker = listedTicker ?? issuer.Presentation?.Listing.Ticker;
        if (db != null)
        {
            var existing = db.Set<EquityListing>()
                .Include(listing => listing.Security)
                    .ThenInclude(security => security.Issuer)
                        .ThenInclude(owner => owner.Presentation)
                .SingleOrDefault(listing =>
                    listing.MarketCountryCode == "US"
                    && listing.Ticker == ticker
                    && listing.Security.EquityIssuerId == issuer.Id
                );
            if (existing != null)
                return existing;
        }
        if (db != null && db.Entry(issuer).State == EntityState.Detached)
            issuer =
                db.Set<EquityIssuer>().Local.FirstOrDefault(row => row.Id == issuer.Id)
                ?? db.Set<EquityIssuer>().SingleOrDefault(row => row.Id == issuer.Id)
                ?? issuer;
        var listing = UsEquityDirectory.GetOrAddListing(issuer, ticker);
        if (db != null && db.Entry(issuer).State == EntityState.Detached)
            db.Add(issuer);
        if (db != null && db.Entry(listing).State == EntityState.Detached)
            db.Add(listing);
        return listing;
    }

    public static EquityListing ForStock(
        DbContext db,
        CommonStock stock,
        string listedTicker = null
    )
    {
        var ticker = listedTicker ?? stock.Ticker;
        if (db == null)
        {
            var cached = Detached.GetOrCreateValue(stock);
            if (cached.TryGetValue(ticker, out var existing))
                return existing;
            var detachedIssuer =
                cached.Values.FirstOrDefault()?.Security.Issuer
                ?? new EquityIssuer
                {
                    Id = stock.Id,
                    Name = stock.Name,
                    Cik = stock.Cik,
                    IndustryId = stock.IndustryId,
                    Industry = stock.Industry,
                };
            var detachedPrimary =
                detachedIssuer.Presentation?.Listing ?? Create(stock, detachedIssuer, stock.Ticker);
            detachedIssuer.Presentation ??= new EquityIssuerPresentation
            {
                Issuer = detachedIssuer,
                Listing = detachedPrimary,
                EquityListingId = detachedPrimary.Id,
            };
            var detached =
                ticker == stock.Ticker ? detachedPrimary : Create(stock, detachedIssuer, ticker);
            cached[ticker] = detached;
            return detached;
        }
        if (db.Database.IsRelational())
        {
            var stored = db.Set<LegacyEquityListing>()
                .Where(row => row.CommonStockId == stock.Id && row.ListedTicker == ticker)
                .Select(row => row.Listing)
                .SingleOrDefault();
            if (stored != null)
                return stored;
            if (db.Entry(stock).State == EntityState.Detached)
                db.Add(stock);
            if (db.Entry(stock).State == EntityState.Added)
                db.SaveChanges();
            db.Database.ExecuteSqlInterpolated(
                $"SELECT public.eq_ensure_legacy_listing({stock.Id}, {ticker})"
            );
            return db.Set<LegacyEquityListing>()
                .Where(row => row.CommonStockId == stock.Id && row.ListedTicker == ticker)
                .Select(row => row.Listing)
                .Single();
        }

        if (db.Entry(stock).State == EntityState.Detached)
            db.Add(stock);
        var existingMapping =
            db.Set<LegacyEquityListing>()
                .Local.FirstOrDefault(row =>
                    row.CommonStockId == stock.Id && row.ListedTicker == ticker
                )
            ?? db.Set<LegacyEquityListing>()
                .Include(row => row.Listing)
                    .ThenInclude(row => row.Security)
                        .ThenInclude(row => row.Issuer)
                            .ThenInclude(row => row.Presentation)
                .FirstOrDefault(row => row.CommonStockId == stock.Id && row.ListedTicker == ticker);
        if (existingMapping != null)
            return existingMapping.Listing;
        var issuer =
            db.Set<EquityIssuer>().Local.FirstOrDefault(row => row.Id == stock.Id)
            ?? db.Set<EquityIssuer>()
                .Include(row => row.Presentation)
                    .ThenInclude(row => row.Listing)
                        .ThenInclude(row => row.Security)
                .SingleOrDefault(row => row.Id == stock.Id)
            ?? new EquityIssuer
            {
                Id = stock.Id,
                Name = stock.Name,
                Cik = stock.Cik,
                IndustryId = stock.IndustryId,
                Industry = stock.Industry,
            };
        var primary = issuer.Presentation?.Listing ?? Create(stock, issuer, stock.Ticker);
        primary.Security ??= new EquitySecurity { Issuer = issuer, EquityIssuerId = issuer.Id };
        primary.Security.Issuer = issuer;
        primary.Security.EquityIssuerId = issuer.Id;
        primary.EquitySecurityId = primary.Security.Id;
        primary.Security.Cusip = stock.Cusip;
        primary.Security.SharesOutstanding = stock.SharesOutStanding;
        primary.Security.MarketCapitalization = stock.MarketCapitalization;
        primary.Security.RegistrationType = stock.ListedSecurityType;
        issuer.Presentation ??= new EquityIssuerPresentation { Issuer = issuer, Listing = primary };
        issuer.Presentation.EquityListingId = primary.Id;
        if (db.Entry(issuer).State == EntityState.Detached)
            db.Add(issuer);
        if (db.Entry(primary).State == EntityState.Detached)
            db.Add(primary);
        var listing =
            ticker == stock.Ticker
                ? primary
                : db.Set<EquityListing>()
                    .Local.FirstOrDefault(row =>
                        row.Security.EquityIssuerId == issuer.Id && row.Ticker == ticker
                    )
                    ?? db.Set<EquityListing>()
                        .FirstOrDefault(row =>
                            row.Security.EquityIssuerId == issuer.Id && row.Ticker == ticker
                        )
                    ?? Create(stock, issuer, ticker);
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
                    ListedTicker = ticker,
                    Listing = listing,
                }
            );
        return listing;
    }

    private static EquityListing Create(CommonStock stock, EquityIssuer issuer, string ticker) =>
        new()
        {
            Ticker = ticker,
            Active = stock.Active,
            DelistedOn = stock.DelistedOn,
            PriceHistoryBackfilled = stock.PriceHistoryBackfilledTickers.Contains(ticker),
            IsDirectoryListed = true,
            IsReferenceListed = stock.ReferenceTickers.Any(reference =>
                string.Equals(
                    TickerNormalizer.NormalizeDashListed(reference),
                    TickerNormalizer.NormalizeDashListed(ticker),
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            Security = new EquitySecurity
            {
                Issuer = issuer,
                EquityIssuerId = issuer.Id,
                Cusip = ticker == stock.Ticker ? stock.Cusip : null,
                SharesOutstanding = ticker == stock.Ticker ? stock.SharesOutStanding : 0,
                MarketCapitalization = ticker == stock.Ticker ? stock.MarketCapitalization : 0,
                RegistrationType = ticker == stock.Ticker ? stock.ListedSecurityType : default,
            },
        };
}
