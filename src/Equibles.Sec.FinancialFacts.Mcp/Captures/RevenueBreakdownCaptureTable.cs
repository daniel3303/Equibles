using Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;

namespace Equibles.Sec.FinancialFacts.Mcp.Captures;

// One printed breakdown table: its title, the members by period (Series, oldest period first as printed), the
// consolidated total row (null cells where the text prints a dash; empty when no total row is printed) and the notes
// printed under it. Additive marks a revenue axis whose rows were checked against the consolidated total in every year
// and never overshot it, so the rows can be stacked without double counting; segment income and margins never are.
public sealed record RevenueBreakdownCaptureTable(
    string Title,
    AxisSeries Series,
    string TotalLabel,
    IReadOnlyList<decimal?> Totals,
    IReadOnlyList<string> Notes,
    bool Additive
);
