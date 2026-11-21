using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Mcp.Captures;

// What GetFinancialStatement printed: one statement for one fiscal period, its lines in the statement's order, and the
// notes under it. FiscalYear is the company's own year label, the one the tool's year argument takes.
public sealed record FinancialStatementCapture(
    EquityIssuer Stock,
    FinancialStatementType Statement,
    int FiscalYear,
    SecFiscalPeriod Period,
    IReadOnlyList<FinancialStatementCaptureLine> Lines,
    IReadOnlyList<string> Notes
);
