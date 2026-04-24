using Equibles.Data;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

public class CommonStockRepository : BaseRepository<CommonStock> {
    public CommonStockRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<CommonStock> Search(string search) {
        var query = GetAll().OrderBy(c => c.Ticker).AsQueryable();

        if (!string.IsNullOrEmpty(search)) {
            foreach (var word in search.Split(" ")) {
                query = query.Where(c =>
                    EF.Functions.ILike(c.Ticker, $"%{word}%") ||
                    EF.Functions.ILike(c.Name, $"%{word}%") ||
                    EF.Functions.ILike(c.Description, $"%{word}%") ||
                    EF.Functions.ILike(c.Industry.Name, $"%{word}%"));
            }
        }

        return query;
    }

    public async Task<CommonStock> GetByCik(string cik) {
        return await GetAll().FirstOrDefaultAsync(cs => cs.Cik == cik);
    }

    public async Task<CommonStock> GetByName(string name) {
        return await GetAll().FirstOrDefaultAsync(cs => cs.Name.ToLower() == name.ToLower());
    }

    public IQueryable<CommonStock> GetByCiks(IEnumerable<string> ciks) {
        return GetAll().Where(cs => ciks.Contains(cs.Cik));
    }

    /// <summary>
    /// Returns the stock whose primary <c>Ticker</c> equals <paramref name="ticker"/>, or whose
    /// <c>SecondaryTickers</c> contains it. A single ticker symbol can legitimately appear on
    /// more than one company (e.g. a preferred-share ticker listed under both the parent REIT
    /// and its operating-partnership SEC filer); when that happens, a primary match is returned
    /// in preference to a secondary match so lookups remain deterministic.
    /// </summary>
    public async Task<CommonStock> GetByTicker(string ticker) {
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
    public async Task<CommonStock> GetByPrimaryTicker(string ticker) {
        return await GetAll().FirstOrDefaultAsync(cs => cs.Ticker == ticker);
    }

    public IQueryable<CommonStock> GetByTickers(IEnumerable<string> tickers) {
        return GetAll().Where(cs =>
            tickers.Contains(cs.Ticker) || cs.SecondaryTickers.Any(st => tickers.Contains(st)));
    }

    public IQueryable<string> GetAllTickers() {
        return GetAll().Select(cs => cs.Ticker);
    }

    public IQueryable<string> GetAllSecondaryTickers() {
        return GetAll()
            .Where(cs => cs.SecondaryTickers.Count > 0)
            .SelectMany(cs => cs.SecondaryTickers);
    }
}
