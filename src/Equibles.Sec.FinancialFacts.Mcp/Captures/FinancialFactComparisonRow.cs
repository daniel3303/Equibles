namespace Equibles.Sec.FinancialFacts.Mcp.Captures;

// One company's printed row of a peer comparison. Ticker is the row's printed label: the caller's ticker, with the
// issuer's display symbol in parentheses when the two differ ("GOOG (GOOGL)"), so it is not a ticker any tool resolves.
// Symbol is that display symbol, which every financial-fact tool resolves.
public sealed record FinancialFactComparisonRow(
    string Ticker,
    string Name,
    FinancialFactCaptureRow Fact,
    string Symbol
);
