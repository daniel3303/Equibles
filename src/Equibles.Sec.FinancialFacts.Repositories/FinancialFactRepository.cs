using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

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

    /// <summary>
    /// The period's own consolidated flow facts that measure the requested granularity, the
    /// spans the flow statements render at (<see cref="StatementLineFacts.MeasuresGranularity"/>
    /// in SQL). Where these end is where the period ends, and the only evidence of it that a
    /// balance sheet, which has no span of its own, can be dated by.
    /// </summary>
    public IQueryable<FinancialFact> GetMeasuredFlows(
        CommonStock stock,
        int fiscalYear,
        SecFiscalPeriod fiscalPeriod,
        IReadOnlyCollection<Guid> flowConceptIds
    )
    {
        return GetConsolidatedByStock(stock)
            .Where(f =>
                f.FiscalYear == fiscalYear
                && f.FiscalPeriod == fiscalPeriod
                && flowConceptIds.Contains(f.FinancialConceptId)
            )
            .Where(StatementLineFacts.MeasuresGranularityInSql(fiscalPeriod));
    }

    /// <summary>
    /// Consolidated point facts stated within <paramref name="toleranceDays"/> of a date, under
    /// ANY fiscal stamp: the importer dates an interim instant by the filer's own fiscal-year
    /// label and a duration by the calendar year its period ends in, so for every filer whose
    /// year is named differently a balance sheet sits one bucket away from its own flows.
    /// </summary>
    public IQueryable<FinancialFact> GetStatedNear(
        CommonStock stock,
        IReadOnlyCollection<Guid> conceptIds,
        DateOnly date,
        int toleranceDays
    )
    {
        var from = date.AddDays(-toleranceDays);
        var to = date.AddDays(toleranceDays);
        return GetConsolidatedByStock(stock)
            .Where(f =>
                conceptIds.Contains(f.FinancialConceptId)
                && f.PeriodEnd == f.PeriodStart
                && f.PeriodEnd >= from
                && f.PeriodEnd <= to
            );
    }
}
