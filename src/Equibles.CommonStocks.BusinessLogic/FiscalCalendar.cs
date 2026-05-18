using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.BusinessLogic;

/// <summary>
/// Maps an arbitrary date to a company's fiscal quarter/year given the month
/// its fiscal year ends in. Replaces ad-hoc calendar-quarter math for
/// off-calendar filers (e.g. Apple ≈ September, Microsoft = June).
/// </summary>
/// <remarks>
/// Quarters are derived purely from the fiscal-year-end <em>month</em>: the
/// fiscal year starts the first day of the following month and each quarter is
/// three calendar months. The fiscal-year-end <em>day</em> is deliberately
/// ignored — many filers use a moving "last Saturday of the month" 52/53-week
/// calendar whose day shifts year to year, so a day-precise period range would
/// be wrong as often as it is right. Month-granularity matches how SEC 10-K /
/// 10-Q reporting periods line up.
/// </remarks>
public static class FiscalCalendar
{
    private const int MonthsPerQuarter = 3;

    /// <summary>
    /// Returns the fiscal period for <paramref name="date"/> given a fiscal
    /// year that ends in <paramref name="fiscalYearEndMonth"/> (1-12, where 12
    /// is a plain calendar-year filer).
    /// </summary>
    public static FiscalPeriod GetPeriod(DateOnly date, int fiscalYearEndMonth)
    {
        if (fiscalYearEndMonth is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fiscalYearEndMonth),
                fiscalYearEndMonth,
                "Fiscal year-end month must be between 1 and 12."
            );
        }

        // The fiscal year is labelled by the calendar year it ends in. A date
        // in or before the end month belongs to a fiscal year ending this
        // calendar year; a date after it belongs to the one ending next year.
        var fiscalYear = date.Month <= fiscalYearEndMonth ? date.Year : date.Year + 1;

        var fiscalStartMonth = fiscalYearEndMonth % 12 + 1;
        var monthsIntoYear = (date.Month - fiscalStartMonth + 12) % 12;
        var quarter = monthsIntoYear / MonthsPerQuarter + 1;

        return new FiscalPeriod(fiscalYear, quarter);
    }

    /// <summary>
    /// Returns the fiscal period for <paramref name="date"/> using the stock's
    /// detected <see cref="CommonStock.FiscalYearEndMonth"/>, or null when the
    /// fiscal year-end has not been detected yet.
    /// </summary>
    public static FiscalPeriod? GetPeriod(DateOnly date, CommonStock commonStock)
    {
        if (commonStock == null)
        {
            throw new ArgumentNullException(nameof(commonStock));
        }

        return commonStock.FiscalYearEndMonth is { } month ? GetPeriod(date, month) : null;
    }
}
