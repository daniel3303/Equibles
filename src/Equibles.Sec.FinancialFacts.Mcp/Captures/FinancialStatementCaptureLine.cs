namespace Equibles.Sec.FinancialFacts.Mcp.Captures;

// One printed statement line. Concept is the alias GetFinancialFact reads the same line through; Basis is the printed
// Reported, Derived quarter, Derived or Sum of reported parts.
public sealed record FinancialStatementCaptureLine(
    string Label,
    string Concept,
    string Basis,
    FinancialFactCaptureRow Fact
);
