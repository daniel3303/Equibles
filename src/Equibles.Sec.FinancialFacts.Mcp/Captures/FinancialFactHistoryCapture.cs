using Equibles.CommonStocks.Data.Models;

namespace Equibles.Sec.FinancialFacts.Mcp.Captures;

// What GetFinancialFact printed: one concept's rows newest first, how many periods matched before maxResults cut them,
// and the notes under the table.
public sealed record FinancialFactHistoryCapture(
    EquityIssuer Stock,
    string Concept,
    bool AsOriginallyReported,
    IReadOnlyList<FinancialFactCaptureRow> Rows,
    int TotalPeriods,
    IReadOnlyList<string> Notes
);
