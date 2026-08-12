namespace Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;

/// <summary>
/// One member row of a pivoted axis: the representative member QName (latest-filed
/// spelling of its fold group), a humanized label, and one value per period column
/// (null where the member did not report). <see cref="PeriodStarts"/> parallels
/// <see cref="Values"/> with each cell's exact duration start, for joins that must not
/// pair same-end/different-span facts.
/// </summary>
public sealed record AxisMemberSeries(
    string Member,
    string Label,
    List<decimal?> Values,
    List<DateOnly?> PeriodStarts = null
)
{
    public DateOnly? PeriodStartAt(int index) =>
        PeriodStarts != null && index < PeriodStarts.Count ? PeriodStarts[index] : null;
}
