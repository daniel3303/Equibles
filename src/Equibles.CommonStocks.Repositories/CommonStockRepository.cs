using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

public class CommonStockRepository : BaseRepository<CommonStock>
{
    public CommonStockRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CommonStock> Search(string search)
    {
        var query = GetAll();

        if (string.IsNullOrEmpty(search))
        {
            return query.OrderBy(c => c.Ticker);
        }

        foreach (var word in search.Split(" "))
        {
            // Escape LIKE metacharacters so a typed '_' or '%' matches literally
            // rather than behaving as a wildcard.
            var pattern = LikePattern.Contains(word);
            query = query.Where(c =>
                EF.Functions.ILike(c.Ticker, pattern, LikePattern.EscapeChar)
                || EF.Functions.ILike(c.Name, pattern, LikePattern.EscapeChar)
                || EF.Functions.ILike(c.Description, pattern, LikePattern.EscapeChar)
                || EF.Functions.ILike(c.Industry.Name, pattern, LikePattern.EscapeChar)
            );
        }

        // Rank an exact ticker hit to the very top, then ticker prefix hits, ahead of the
        // alphabetical fallback — so a typed symbol (e.g. "ARE") leads its group instead of
        // sorting past a per-group result cap alphabetically (ARE would otherwise fall behind
        // ABXXF/ACHC/…). Both comparisons run through ILike so they stay case-insensitive and
        // metacharacter-safe like the filter above; ordering by the boolean puts matches first.
        var exact = LikePattern.Escape(search.Trim());
        var prefix = LikePattern.StartsWith(search.Trim());
        return query
            .OrderByDescending(c => EF.Functions.ILike(c.Ticker, exact, LikePattern.EscapeChar))
            .ThenByDescending(c => EF.Functions.ILike(c.Ticker, prefix, LikePattern.EscapeChar))
            .ThenBy(c => c.Ticker);
    }

    public async Task<CommonStock> GetByCik(string cik)
    {
        return await GetAll().FirstOrDefaultAsync(cs => cs.Cik == cik);
    }

    public async Task<CommonStock> GetByName(string name)
    {
        return await GetAll().FirstOrDefaultAsync(cs => cs.Name.ToLower() == name.ToLower());
    }

    public IQueryable<CommonStock> GetByCiks(IEnumerable<string> ciks)
    {
        return GetAll().Where(cs => ciks.Contains(cs.Cik));
    }

    /// <summary>
    /// Returns the stock whose primary <c>Cik</c> equals <paramref name="cik"/>, or whose
    /// <c>SecondaryCiks</c> contains it. Primary matches are preferred so lookups remain
    /// deterministic when a subsidiary CIK is attached to a parent that's also queryable
    /// by its own primary CIK.
    /// </summary>
    public async Task<CommonStock> GetByAnyCik(string cik)
    {
        return await GetAll()
            .Where(cs => cs.Cik == cik || cs.SecondaryCiks.Contains(cik))
            .OrderBy(cs => cs.Cik == cik ? 0 : 1)
            .FirstOrDefaultAsync();
    }

    public IQueryable<string> GetAllSecondaryCiks()
    {
        return GetAll().Where(cs => cs.SecondaryCiks.Count > 0).SelectMany(cs => cs.SecondaryCiks);
    }

    /// <summary>
    /// Returns the stock whose primary <c>Ticker</c> equals <paramref name="ticker"/>, or whose
    /// <c>SecondaryTickers</c> contains it. A single ticker symbol can legitimately appear on
    /// more than one company (e.g. a preferred-share ticker listed under both the parent REIT
    /// and its operating-partnership SEC filer); when that happens, a primary match is returned
    /// in preference to a secondary match so lookups remain deterministic.
    /// </summary>
    public async Task<CommonStock> GetByTicker(string ticker)
    {
        return await GetAll()
            .Where(cs => cs.Ticker == ticker || cs.SecondaryTickers.Contains(ticker))
            .OrderBy(cs => cs.Ticker == ticker ? 0 : 1)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Returns the stock whose primary <c>Ticker</c> equals <paramref name="ticker"/>.
    /// Primary tickers are globally unique, so at most one row can match. Use this when the
    /// caller needs to enforce primary-ticker uniqueness rather than the more permissive
    /// primary-or-secondary lookup provided by <see cref="GetByTicker"/>.
    /// </summary>
    public async Task<CommonStock> GetByPrimaryTicker(string ticker)
    {
        return await GetAll().FirstOrDefaultAsync(cs => cs.Ticker == ticker);
    }

    public IQueryable<CommonStock> GetByTickers(IEnumerable<string> tickers)
    {
        return GetAll()
            .Where(cs =>
                tickers.Contains(cs.Ticker) || cs.SecondaryTickers.Any(st => tickers.Contains(st))
            );
    }

    public IQueryable<CommonStock> GetByIds(IEnumerable<Guid> ids)
    {
        return GetAll().Where(cs => ids.Contains(cs.Id));
    }

    public IQueryable<string> GetAllTickers()
    {
        return GetAll().Select(cs => cs.Ticker);
    }

    public IQueryable<string> GetAllSecondaryTickers()
    {
        return GetAll()
            .Where(cs => cs.SecondaryTickers.Count > 0)
            .SelectMany(cs => cs.SecondaryTickers);
    }

    /// <summary>
    /// Retired CUSIPs recorded when a stock's CUSIP changed. They belong to the
    /// CommonStock aggregate (no independent lifecycle), so access lives here
    /// rather than in a dedicated repository.
    /// </summary>
    public IQueryable<CommonStockCusipAlias> GetCusipAliases()
    {
        return DbContext.Set<CommonStockCusipAlias>().AsQueryable();
    }

    public CommonStockCusipAlias AddCusipAlias(CommonStockCusipAlias alias)
    {
        DbContext.Set<CommonStockCusipAlias>().Add(alias);
        return alias;
    }
}
