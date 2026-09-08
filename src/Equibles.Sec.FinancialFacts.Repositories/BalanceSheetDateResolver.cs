using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Repositories;

/// <summary>
/// Where a fiscal period's balance sheet is dated: at the end of the period's own consolidated
/// flows, matched to the fullest stated instant date within
/// <see cref="StatementLineFacts.BalanceSheetDateToleranceDays"/> of it, from any fiscal
/// stamp. One resolver for the MCP tool, the web tab and the commercial card, so the three
/// cannot date one balance sheet three ways.
/// </summary>
public static class BalanceSheetDateResolver
{
    /// <summary>
    /// Null when the period measures no flow at the requested granularity, or no balance
    /// sheet is stated within the tolerance of where they end; the caller then keeps its
    /// bucket-and-anchor path.
    /// </summary>
    public static async Task<DateOnly?> Resolve(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        CommonStock stock,
        int fiscalYear,
        SecFiscalPeriod fiscalPeriod,
        CancellationToken cancellationToken = default
    )
    {
        var balanceSheetLines = FinancialStatementConcepts.For(FinancialStatementType.BalanceSheet);
        var flowLines = FinancialStatementConcepts
            .For(FinancialStatementType.IncomeStatement)
            .Concat(FinancialStatementConcepts.For(FinancialStatementType.CashFlow))
            .ToList();
        var (taxonomies, tags) = StatementLineFacts.CollectConceptPairs(
            balanceSheetLines.Concat(flowLines)
        );

        // GetMatching returns the taxonomy x tag cross product; narrow to the exact pairs so
        // an unrelated concept sharing a tag name can neither end the period nor state a date.
        var concepts = await financialConceptRepository
            .GetMatching(taxonomies, tags)
            .Select(c => new
            {
                c.Id,
                c.Taxonomy,
                c.Tag,
            })
            .ToListAsync(cancellationToken);
        var conceptIdByKey = concepts.ToDictionary(c => (c.Taxonomy, c.Tag), c => c.Id);
        var balanceSheetConceptIds = ConceptIdsFor(conceptIdByKey, balanceSheetLines);
        var flowConceptIds = ConceptIdsFor(conceptIdByKey, flowLines);
        if (balanceSheetConceptIds.Count == 0 || flowConceptIds.Count == 0)
            return null;

        // Distinct (date, concept) pairs on both sides, counted here: each is a handful of
        // dates, and a grouped count with DISTINCT is the kind of shape that stops translating
        // on a provider upgrade. The flow end is weighed the same way the stated date is, or
        // one re-stamped span ending latest dates the sheet by itself.
        var measured = await financialFactRepository
            .GetMeasuredFlows(stock, fiscalYear, fiscalPeriod, flowConceptIds)
            .Select(f => new { f.PeriodEnd, f.FinancialConceptId })
            .Distinct()
            .ToListAsync(cancellationToken);
        var flowPeriodEnd = StatementLineFacts.PickFlowPeriodEnd(
            CountByDate(measured.Select(m => (m.PeriodEnd, m.FinancialConceptId)))
        );
        if (flowPeriodEnd is not { } periodEnd)
            return null;

        var stated = await financialFactRepository
            .GetStatedNear(
                stock,
                balanceSheetConceptIds,
                periodEnd,
                StatementLineFacts.BalanceSheetDateToleranceDays
            )
            .Select(f => new { f.PeriodEnd, f.FinancialConceptId })
            .Distinct()
            .ToListAsync(cancellationToken);

        return StatementLineFacts.PickBalanceSheetDate(
            periodEnd,
            CountByDate(stated.Select(s => (s.PeriodEnd, s.FinancialConceptId)))
        );
    }

    private static List<(DateOnly Date, int ConceptCount)> CountByDate(
        IEnumerable<(DateOnly PeriodEnd, Guid FinancialConceptId)> pairs
    )
    {
        return pairs
            .GroupBy(p => p.PeriodEnd)
            .Select(g => (Date: g.Key, ConceptCount: g.Count()))
            .ToList();
    }

    private static List<Guid> ConceptIdsFor(
        IReadOnlyDictionary<(FactTaxonomy Taxonomy, string Tag), Guid> conceptIdByKey,
        IEnumerable<StatementLine> lines
    )
    {
        return lines
            .SelectMany(l => l.Concepts)
            .Select(c =>
                conceptIdByKey.TryGetValue((c.Taxonomy, c.Tag), out var id) ? id : (Guid?)null
            )
            .Where(id => id != null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
    }
}
