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

    public async Task<CommonStock> GetByTicker(string ticker) {
        return await GetAll().FirstOrDefaultAsync(cs =>
            cs.Ticker == ticker || cs.SecondaryTickers.Contains(ticker));
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
