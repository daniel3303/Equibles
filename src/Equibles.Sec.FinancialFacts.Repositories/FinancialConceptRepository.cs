using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class FinancialConceptRepository : BaseRepository<FinancialConcept>
{
    public FinancialConceptRepository(EquiblesDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// Concepts whose taxonomy and tag fall within the given sets. The caller
    /// narrows further to exact (taxonomy, tag) pairs in memory — bounding the
    /// result to the concepts actually present in the payload being imported.
    /// </summary>
    public IQueryable<FinancialConcept> GetMatching(
        IReadOnlyCollection<FactTaxonomy> taxonomies,
        IReadOnlyCollection<string> tags
    )
    {
        return GetAll().Where(c => taxonomies.Contains(c.Taxonomy) && tags.Contains(c.Tag));
    }
}
