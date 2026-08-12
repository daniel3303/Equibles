namespace Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;

/// <summary>
/// One TWO-dimensional revenue fact (a cross-cut such as product × segment). Filers can
/// move a disaggregation to two-dimensional tagging entirely, leaving an axis family with
/// no single-axis facts; the roll-up in
/// <see cref="RevenueBreakdownCore.RollUpCrossCuts"/> reconstructs the family from these.
/// </summary>
public sealed record CrossCutRevenueRow(
    string AxisA,
    string MemberA,
    string AxisB,
    string MemberB,
    DateOnly PeriodEnd,
    decimal Value,
    string Unit,
    DateOnly FiledDate,
    DateOnly? PeriodStart = null,
    int FiscalYear = 0
);
