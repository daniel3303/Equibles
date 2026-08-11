using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.Repositories;

public class DailyStockPriceRepository : BaseRepository<DailyStockPrice>
{
    public DailyStockPriceRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// Current-primary series only. Existing issuer-level consumers deliberately retain
    /// their original semantics after independently keyed listed-symbol rows are added. Legacy
    /// rows are never guessed into the current primary because their source listing is ambiguous.
    /// </summary>
    public override IQueryable<DailyStockPrice> GetAll()
    {
        return GetAllSeries().Where(p => p.ListedTicker == p.CommonStock.Ticker);
    }

    /// <summary>
    /// Every exact price series, including authoritative secondary tickers. The entity maps
    /// only to the isolated exact-listing table, so legacy ambiguous rows are never exposed.
    /// </summary>
    public IQueryable<DailyStockPrice> GetAllSeries()
    {
        return base.GetAll();
    }

    public IQueryable<DailyStockPrice> GetByStock(CommonStock stock)
    {
        return GetAll().Where(p => p.CommonStockId == stock.Id);
    }

    /// <summary>Prices for the exact listed ticker requested on a filer's row.</summary>
    public IQueryable<DailyStockPrice> GetByStock(CommonStock stock, string ticker)
    {
        var resolvedTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, ticker);
        if (resolvedTicker == null)
            return GetAllSeries().Where(_ => false);

        return GetAllSeries()
            .Where(p => p.CommonStockId == stock.Id && p.ListedTicker == resolvedTicker);
    }

    public IQueryable<DailyStockPrice> GetByStock(
        CommonStock stock,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetAll()
            .Where(p => p.CommonStockId == stock.Id && p.Date >= startDate && p.Date <= endDate);
    }

    public IQueryable<DailyStockPrice> GetByStock(
        CommonStock stock,
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
    public IQueryable<DailyStockPrice> GetTradedByStock(CommonStock stock, string ticker)
    {
        return GetByStock(stock, ticker).Where(p => p.Volume > 0);
    }

    public IQueryable<DailyStockPrice> GetTradedByStock(CommonStock stock)
    {
        return GetByStock(stock).Where(p => p.Volume > 0);
    }

    public IQueryable<DailyStockPrice> GetTradedByStock(
        CommonStock stock,
        string ticker,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetTradedByStock(stock, ticker).Where(p => p.Date >= startDate && p.Date <= endDate);
    }

    public IQueryable<DailyStockPrice> GetTradedByStock(
        CommonStock stock,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetTradedByStock(stock).Where(p => p.Date >= startDate && p.Date <= endDate);
    }

    public IQueryable<DailyStockPrice> GetTradedByStocks(
        IEnumerable<Guid> stockIds,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetByStocks(stockIds, startDate, endDate).Where(p => p.Volume > 0);
    }

    public IQueryable<DailyStockPrice> GetByStocks(
        IEnumerable<Guid> stockIds,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetAll()
            .Where(p =>
                stockIds.Contains(p.CommonStockId) && p.Date >= startDate && p.Date <= endDate
            );
    }

    public IQueryable<DateOnly> GetLatestDate(CommonStock stock)
    {
        return GetAll().Where(p => p.CommonStockId == stock.Id).LatestValue(p => p.Date);
    }

    public IQueryable<DateOnly> GetLatestDate(CommonStock stock, string ticker)
    {
        return GetByStock(stock, ticker).LatestValue(p => p.Date);
    }

    public IQueryable<DateOnly> GetLatestDateAcrossAllStocks()
    {
        return GetAll().LatestValue(p => p.Date, distinct: true);
    }
}
