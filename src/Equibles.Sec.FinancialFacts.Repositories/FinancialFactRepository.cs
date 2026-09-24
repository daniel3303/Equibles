using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class FinancialFactRepository : BaseRepository<FinancialFact>
{
    public FinancialFactRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FinancialFact> GetByIssuerId(Guid issuerId)
    {
        return GetAll().Where(f => f.EquityIssuerId == issuerId);
    }

    public IQueryable<FinancialFact> GetByIssuerIds(IReadOnlyCollection<Guid> issuerIds)
    {
        return GetAll().Where(f => issuerIds.Contains(f.EquityIssuerId));
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
    /// this, not <see cref="GetByIssuerId"/>.
    /// </summary>
    public IQueryable<FinancialFact> GetConsolidatedByIssuerId(Guid issuerId)
    {
        return WithoutByNatureCostOfSales(ConsolidatedByIssuerId(issuerId));
    }

    /// <inheritdoc cref="GetConsolidatedByIssuerId"/>
    public IQueryable<FinancialFact> GetConsolidatedByIssuerIds(IReadOnlyCollection<Guid> issuerIds)
    {
        return WithoutByNatureCostOfSales(
            GetByIssuerIds(issuerIds).Where(f => f.DimensionsKey == "")
        );
    }

    // Sheet dating reads spans, not a cost line, so it skips the cost-of-sales gate and keeps
    // its index-friendly predicate.
    private IQueryable<FinancialFact> ConsolidatedByIssuerId(Guid issuerId)
    {
        return GetByIssuerId(issuerId).Where(f => f.DimensionsKey == "");
    }

    /// <summary>
    /// Drops an IFRS <c>CostOfSales</c> fact unless the same filing states a function-of-expense
    /// line for the same period, since only that presentation (IAS 1.103) makes it the whole cost of revenue.
    /// </summary>
    private IQueryable<FinancialFact> WithoutByNatureCostOfSales(
        IQueryable<FinancialFact> consolidated
    )
    {
        // Excluded ids are one hashed subplan over the scoped cost-of-sales rows, so no read
        // joins the concept table per fact.
        var concepts = GetConcepts();
        var costOfSalesIds = concepts
            .Where(c => c.Taxonomy == FactTaxonomy.IfrsFull && c.Tag == IfrsCostOfSalesTag)
            .Select(c => c.Id);
        var functionOfExpenseIds = concepts
            .Where(c =>
                c.Taxonomy == FactTaxonomy.IfrsFull && IfrsFunctionOfExpenseTags.Contains(c.Tag)
            )
            .Select(c => c.Id);
        // The proof stays correlated only; repeating the issuer list in it steers Postgres to the
        // slower concept/period index.
        var all = GetAll();
        var byNature = consolidated
            .Where(c => costOfSalesIds.Contains(c.FinancialConceptId))
            .Where(c =>
                !all.Any(p =>
                    p.EquityIssuerId == c.EquityIssuerId
                    && p.AccessionNumber == c.AccessionNumber
                    && p.PeriodStart == c.PeriodStart
                    && p.PeriodEnd == c.PeriodEnd
                    && p.DimensionsKey == ""
                    && functionOfExpenseIds.Contains(p.FinancialConceptId)
                )
            )
            .Select(c => c.Id);
        return consolidated.Where(f => !byNature.Contains(f.Id));
    }

    protected virtual IQueryable<FinancialConcept> GetConcepts() =>
        DbContext.Set<FinancialConcept>();

    private const string IfrsCostOfSalesTag = "CostOfSales";

    // Lines only a function-of-expense income statement carries.
    private static readonly string[] IfrsFunctionOfExpenseTags =
    [
        "GrossProfit",
        "DistributionCosts",
        "AdministrativeExpense",
        "SellingGeneralAndAdministrativeExpense",
        "SellingExpense",
        "SalesAndMarketingExpense",
        "GeneralAndAdministrativeExpense",
        "OtherExpenseByFunction",
    ];

    /// <summary>
    /// The period's own consolidated flow facts that measure the requested granularity, the
    /// spans the flow statements render at (<see cref="StatementLineFacts.MeasuresGranularity"/>
    /// in SQL). Where these end is where the period ends, and the only evidence of it that a
    /// balance sheet, which has no span of its own, can be dated by.
    /// </summary>
    public IQueryable<FinancialFact> GetMeasuredFlows(
        Guid issuerId,
        int fiscalYear,
        SecFiscalPeriod fiscalPeriod,
        IReadOnlyCollection<Guid> flowConceptIds
    )
    {
        return ConsolidatedByIssuerId(issuerId)
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
        Guid issuerId,
        IReadOnlyCollection<Guid> conceptIds,
        DateOnly date,
        int toleranceDays
    )
    {
        var from = date.AddDays(-toleranceDays);
        var to = date.AddDays(toleranceDays);
        return ConsolidatedByIssuerId(issuerId)
            .Where(f =>
                conceptIds.Contains(f.FinancialConceptId)
                && f.PeriodEnd == f.PeriodStart
                && f.PeriodEnd >= from
                && f.PeriodEnd <= to
            );
    }
}
