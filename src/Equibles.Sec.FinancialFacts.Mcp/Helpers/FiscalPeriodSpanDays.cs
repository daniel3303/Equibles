namespace Equibles.Sec.FinancialFacts.Mcp.Helpers;

/// <summary>
/// Reporting-period span guards used to tell a discrete fiscal quarter from a
/// full year or a year-to-date span, sized for 52/53-week and 4-4-5 calendars
/// with headroom: a discrete quarter runs 13 weeks (14 in a 53-week year's long
/// quarter), and an ordinary fiscal year stays within 350–380 days. A duration between
/// <see cref="MaxDiscreteQuarterDays"/> and <see cref="MinAnnualSpanDays"/> is a
/// year-to-date span; one above <see cref="MaxAnnualSpanDays"/> is multi-year or
/// inception-to-date. Neither is a quarter or fiscal year.
/// </summary>
internal static class FiscalPeriodSpanDays
{
    internal const int MinDiscreteQuarterDays = 80;
    internal const int MaxDiscreteQuarterDays = 100;
    internal const int MinAnnualSpanDays = 350;
    internal const int MaxAnnualSpanDays = 380;
}
