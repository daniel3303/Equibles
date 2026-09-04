using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Data;

namespace Equibles.Congress.Repositories;

public class CongressionalTradeRepository : BaseRepository<CongressionalTrade>
{
    public CongressionalTradeRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CongressionalTrade> GetByStock(CommonStock stock)
    {
        return GetAll().Where(t => t.CommonStockId == stock.Id);
    }

    public IQueryable<CongressionalTrade> GetByListing(CommonStock stock, string listedTicker)
    {
        var isPrimary = string.Equals(listedTicker, stock.Ticker, StringComparison.OrdinalIgnoreCase);
        return GetAll().Where(t =>
            t.CommonStockId == stock.Id
            && (t.FiledTicker == listedTicker || (isPrimary && t.FiledTicker == ""))
        );
    }

    public IQueryable<CongressionalTrade> GetByStock(CommonStock stock, DateOnly from, DateOnly to)
    {
        return GetAll()
            .Where(t =>
                t.CommonStockId == stock.Id && t.TransactionDate >= from && t.TransactionDate <= to
            );
    }

    public IQueryable<CongressionalTrade> GetByListing(
        CommonStock stock,
        string listedTicker,
        DateOnly from,
        DateOnly to
    ) => GetByListing(stock, listedTicker)
        .Where(t => t.TransactionDate >= from && t.TransactionDate <= to);

    public IQueryable<CongressionalTrade> GetByMember(CongressMember member)
    {
        return GetAll().Where(t => t.CongressMemberId == member.Id);
    }
}
