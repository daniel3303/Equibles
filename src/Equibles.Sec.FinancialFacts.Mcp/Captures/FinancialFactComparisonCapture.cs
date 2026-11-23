using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Mcp.Captures;

// What CompareFinancialFact printed for one concept and fiscal period: each company's row in the table's order and the
// notes under it. Calendar marks a comparison by calendar year: FiscalYear is then that calendar year, and each row's
// own FiscalYear is the company's label for the fiscal year placed in it.
public sealed record FinancialFactComparisonCapture(
    string Concept,
    int FiscalYear,
    SecFiscalPeriod Period,
    IReadOnlyList<FinancialFactComparisonRow> Rows,
    IReadOnlyList<string> Notes,
    bool Calendar = false
);
