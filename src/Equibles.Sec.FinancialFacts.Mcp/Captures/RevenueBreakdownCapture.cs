using Equibles.CommonStocks.Data.Models;

namespace Equibles.Sec.FinancialFacts.Mcp.Captures;

// What GetRevenueBreakdown printed: each table in the answer's order and the notes above them.
public sealed record RevenueBreakdownCapture(
    EquityIssuer Stock,
    IReadOnlyList<RevenueBreakdownCaptureTable> Tables,
    IReadOnlyList<string> Notes
);
