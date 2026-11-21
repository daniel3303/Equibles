using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Mcp.Captures;

// What CompareFinancialFact printed for one concept and fiscal period: each company's row in the table's order and the
// notes under it.
public sealed record FinancialFactComparisonCapture(
    string Concept,
    int FiscalYear,
    SecFiscalPeriod Period,
    IReadOnlyList<FinancialFactComparisonRow> Rows,
    IReadOnlyList<string> Notes
);
