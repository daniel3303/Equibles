namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Span guards that tell a discrete fiscal quarter from a full year or a year-to-date span, sized for
/// 52/53-week and 4-4-5 calendars: a span between <see cref="MaxQuarter"/> and <see cref="MinAnnual"/>
/// is year-to-date, one above <see cref="MaxAnnual"/> multi-year or inception-to-date.
/// <see cref="MaxQuarter"/> admits a 16- or 17-week quarter (Costco's fourth, Kroger's first);
/// twice it stays below a nine-month span, so a half-year is never read as one quarter.
/// </summary>
public static class FiscalPeriodSpanDays
{
    public const int MinQuarter = 80;
    public const int MaxQuarter = 119;
    public const int MinAnnual = 350;
    public const int MaxAnnual = 380;
}
