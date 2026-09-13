using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.Repositories;

public class EquityDailyStockPriceRepository : BaseRepository<EquityDailyStockPrice>
{
    public EquityDailyStockPriceRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// Current-primary series only. Existing issuer-level consumers deliberately retain
    /// their original semantics after independently keyed listed-symbol rows are added. Legacy
    /// rows are never guessed into the current primary because their source listing is ambiguous.
    /// </summary>
    public virtual IQueryable<EquityDailyStockPrice> GetPrimarySeries()
    {
        return GetAllSeries()
            .Where(p =>
                p.EquityListingId == p.Listing.Security.Issuer.Presentation.EquityListingId
            );
    }

    /// <summary>
    /// Every exact price series, including authoritative secondary tickers. The entity maps
    /// only to the isolated exact-listing table, so legacy ambiguous rows are never exposed.
    /// </summary>
    public virtual IQueryable<EquityDailyStockPrice> GetAllSeries()
    {
        return base.GetAll();
    }

    /// <summary>Read native histories by stable listing ID; the result preserves the existing price contract.</summary>
    public IQueryable<EquityDailyStockPrice> GetByListing(Guid listingId) =>
        GetAllSeries().Where(price => price.EquityListingId == listingId);

    public IQueryable<EquityListing> GetUsListingReferences(IEnumerable<Guid> issuerIds) =>
        DbContext
            .Set<EquityListing>()
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && issuerIds.Contains(listing.Security.EquityIssuerId)
            );

    public IQueryable<EquityListing> GetUsListingReference(Guid listingId) =>
        DbContext
            .Set<EquityListing>()
            .Where(listing => listing.Id == listingId && listing.MarketCountryCode == "US");

    public virtual IQueryable<EquityDailyStockPrice> GetUsSeries() =>
        GetAllSeries().Where(price => price.Listing.MarketCountryCode == "US");

    public IQueryable<EquityDailyStockPrice> GetUsSeries(Guid issuerId, string ticker) =>
        GetUsSeries()
            .Where(price =>
                price.Listing.Security.EquityIssuerId == issuerId
                && price.Listing.Ticker == ticker
                && !DbContext
                    .Set<EquityListing>()
                    .Any(other =>
                        other.Id != price.EquityListingId
                        && other.MarketCountryCode == "US"
                        && other.Ticker == ticker
                        && other.Security.EquityIssuerId == issuerId
                    )
            );

    public IQueryable<EquityDailyStockPrice> GetByStock(EquityIssuer stock)
    {
        return GetPrimarySeries().Where(p => p.Listing.Security.EquityIssuerId == stock.Id);
    }

    /// <summary>Prices for the exact listed ticker requested on a filer's row.</summary>
    public IQueryable<EquityDailyStockPrice> GetByStock(EquityIssuer stock, string ticker)
    {
        var resolvedTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, ticker);
        if (resolvedTicker == null)
            return GetAllSeries().Where(_ => false);

        return GetUsSeries(stock.Id, resolvedTicker);
    }

    public IQueryable<EquityDailyStockPrice> GetByStock(
        EquityIssuer stock,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetPrimarySeries()
            .Where(p =>
                p.Listing.Security.EquityIssuerId == stock.Id
                && p.Date >= startDate
                && p.Date <= endDate
            );
    }

    public IQueryable<EquityDailyStockPrice> GetByStock(
        EquityIssuer stock,
        string ticker,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetByStock(stock, ticker).Where(p => p.Date >= startDate && p.Date <= endDate);
    }

    /// <summary>
    /// Exact-listing rows backed by at least one reported trade. Upstream daily feeds can emit
    /// zero-volume carry-forward candles for a dormant symbol; customer-facing price surfaces
    /// must not present those synthetic rows as a newly settled market price.
    /// </summary>
    public IQueryable<EquityDailyStockPrice> GetTradedByStock(EquityIssuer stock, string ticker)
    {
        return GetByStock(stock, ticker).Where(p => p.Volume > 0);
    }

    public IQueryable<EquityDailyStockPrice> GetTradedByStock(EquityIssuer stock)
    {
        return GetByStock(stock).Where(p => p.Volume > 0);
    }

    public IQueryable<EquityDailyStockPrice> GetTradedByStock(
        EquityIssuer stock,
        string ticker,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetTradedByStock(stock, ticker).Where(p => p.Date >= startDate && p.Date <= endDate);
    }

    public IQueryable<EquityDailyStockPrice> GetTradedByStock(
        EquityIssuer stock,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetTradedByStock(stock).Where(p => p.Date >= startDate && p.Date <= endDate);
    }

    public IQueryable<EquityDailyStockPrice> GetTradedByStocks(
        IEnumerable<Guid> stockIds,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetByStocks(stockIds, startDate, endDate).Where(p => p.Volume > 0);
    }

    public IQueryable<EquityDailyStockPrice> GetByStocks(
        IEnumerable<Guid> stockIds,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetPrimarySeries()
            .Where(p =>
                stockIds.Contains(p.Listing.Security.EquityIssuerId)
                && p.Date >= startDate
                && p.Date <= endDate
            );
    }

    public IQueryable<DateOnly> GetLatestDate(EquityIssuer stock)
    {
        return GetPrimarySeries()
            .Where(p => p.Listing.Security.EquityIssuerId == stock.Id)
            .LatestValue(p => p.Date);
    }

    public IQueryable<DateOnly> GetLatestDate(EquityIssuer stock, string ticker)
    {
        return GetByStock(stock, ticker).LatestValue(p => p.Date);
    }

    public IQueryable<DateOnly> GetLatestDateAcrossAllStocks()
    {
        return GetPrimarySeries().LatestValue(p => p.Date, distinct: true);
    }
}
