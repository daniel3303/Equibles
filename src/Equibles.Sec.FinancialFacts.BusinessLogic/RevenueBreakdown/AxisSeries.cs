namespace Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;

/// <summary>
/// One breakdown axis pivoted into period-end columns (oldest first) × member rows, in a
/// single pinned unit. An axis with no members means the company reports nothing on it.
/// </summary>
public sealed record AxisSeries(
    string Unit,
    List<DateOnly> PeriodEnds,
    List<AxisMemberSeries> Members
);
