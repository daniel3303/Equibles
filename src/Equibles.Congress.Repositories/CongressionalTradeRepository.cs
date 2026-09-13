using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Data;

namespace Equibles.Congress.Repositories;

public class CongressionalTradeRepository : BaseRepository<CongressionalTrade>
{
    public CongressionalTradeRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CongressionalTrade> GetByStock(EquityIssuer stock)
    {
        return GetAll().Where(t => t.EquityIssuerId == stock.Id);
    }

    public IQueryable<CongressionalTrade> GetByListing(EquityIssuer stock, string listedTicker)
    {
        var isPrimary = string.Equals(
            listedTicker,
            stock.Presentation.Listing.Ticker,
            StringComparison.OrdinalIgnoreCase
        );
        return GetAll()
            .Where(t =>
                t.EquityIssuerId == stock.Id
                && (t.FiledTicker == listedTicker || (isPrimary && t.FiledTicker == ""))
            );
    }

    public IQueryable<CongressionalTrade> GetByStock(EquityIssuer stock, DateOnly from, DateOnly to)
    {
        return GetAll()
            .Where(t =>
                t.EquityIssuerId == stock.Id && t.TransactionDate >= from && t.TransactionDate <= to
            );
    }

    public IQueryable<CongressionalTrade> GetByListing(
        EquityIssuer stock,
        string listedTicker,
        DateOnly from,
        DateOnly to
    ) =>
        GetByListing(stock, listedTicker)
            .Where(t => t.TransactionDate >= from && t.TransactionDate <= to);

    public IQueryable<CongressionalTrade> GetByMember(CongressMember member)
    {
        return GetAll().Where(t => t.CongressMemberId == member.Id);
    }
}
