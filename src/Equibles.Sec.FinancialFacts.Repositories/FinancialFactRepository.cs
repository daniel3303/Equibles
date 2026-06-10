using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class FinancialFactRepository : BaseRepository<FinancialFact>
{
    public FinancialFactRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FinancialFact> GetByStock(CommonStock stock)
    {
        return GetAll().Where(f => f.CommonStockId == stock.Id);
    }

    public IQueryable<FinancialFact> GetByStocks(IReadOnlyCollection<Guid> stockIds)
    {
        return GetAll().Where(f => stockIds.Contains(f.CommonStockId));
    }

    /// <summary>
    /// Facts for the consolidated (no-dimension) context only — the figures the
    /// SEC Company Facts API reports, identified by an empty
    /// <see cref="FinancialFact.DimensionsKey"/>. Excludes the dimensional
    /// segment/geography/product rows the XBRL extractor adds: those share a
    /// concept, period and accession with their consolidated sibling, so a
    /// per-concept "latest filed" collapse would otherwise pick a segment value
    /// (e.g. iPhone revenue) in place of the total non-deterministically. Every
    /// statement/figure read path that renders consolidated numbers must use
    /// this, not <see cref="GetByStock"/>.
    /// </summary>
    public IQueryable<FinancialFact> GetConsolidatedByStock(CommonStock stock)
    {
        return GetByStock(stock).Where(f => f.DimensionsKey == "");
    }

    /// <inheritdoc cref="GetConsolidatedByStock"/>
    public IQueryable<FinancialFact> GetConsolidatedByStocks(IReadOnlyCollection<Guid> stockIds)
    {
        return GetByStocks(stockIds).Where(f => f.DimensionsKey == "");
    }
}
