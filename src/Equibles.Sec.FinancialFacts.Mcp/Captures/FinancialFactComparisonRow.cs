namespace Equibles.Sec.FinancialFacts.Mcp.Captures;

// One company's printed row of a peer comparison: the ticker as the caller asked for it and the company's figure.
public sealed record FinancialFactComparisonRow(
    string Ticker,
    string Name,
    FinancialFactCaptureRow Fact
);
