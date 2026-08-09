using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class FailToDeliverRepository : BaseRepository<FailToDeliver>
{
    public FailToDeliverRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FailToDeliver> GetByStock(CommonStock stock)
    {
        return GetAll().Where(f => f.CommonStockId == stock.Id);
    }

    public IQueryable<DateOnly> GetLatestDate()
    {
        return GetAll().LatestValue(f => f.SettlementDate, distinct: true);
    }

    /// <summary>
    /// The earliest settlement date on file across the whole table — the ingest lane is
    /// forward-only from its first run, so this is the data's coverage floor. Absence
    /// of a date can only be read as "no reported fails" INSIDE the covered window;
    /// surfaces must scope that claim with this value.
    /// </summary>
    public IQueryable<DateOnly> GetEarliestDate()
    {
        return GetAll().OrderBy(f => f.SettlementDate).Select(f => f.SettlementDate).Take(1);
    }
}
