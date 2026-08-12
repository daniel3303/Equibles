namespace Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;

/// <summary>
/// One single-axis dimensional revenue (or segment income) fact, projected for the
/// revenue-breakdown selection. <paramref name="PeriodStart"/> keeps the exact duration
/// so a downstream ratio cannot join same-end/different-span facts;
/// <paramref name="FiscalYear"/> carries the filer's own period stamp for consumers that
/// label columns by fiscal year.
/// </summary>
public sealed record DimensionalRevenueRow(
    string Axis,
    string Member,
    DateOnly PeriodEnd,
    decimal Value,
    string Unit,
    DateOnly FiledDate,
    DateOnly? PeriodStart = null,
    int FiscalYear = 0
);
