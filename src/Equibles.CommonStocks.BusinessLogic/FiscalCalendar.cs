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
    /// Inverse of <see cref="GetPeriod(DateOnly,int)"/>: the last calendar day
    /// of <paramref name="fiscalQuarter"/> (1-4) of fiscal year
    /// <paramref name="fiscalYear"/>, for a company whose fiscal year ends in
    /// <paramref name="fiscalYearEndMonth"/> (1-12, 12 = calendar-year filer).
    /// </summary>
    /// <remarks>
    /// Month-granular by design — the day is the last of the quarter's end
    /// month, matching how <see cref="GetPeriod(DateOnly,int)"/> buckets dates
    /// and how SEC 10-Q/10-K reporting periods line up. 52/53-week filers whose
    /// period end drifts a few days are intentionally normalised to month-end
    /// (see the type remarks). Round-trips with <see cref="GetPeriod(DateOnly,int)"/>:
    /// <c>GetPeriod(GetQuarterEndDate(y, q, m), m) == new FiscalPeriod(y, q)</c>.
    /// </remarks>
    public static DateOnly GetQuarterEndDate(
        int fiscalYear,
        int fiscalQuarter,
        int fiscalYearEndMonth
    )
    {
        if (fiscalYearEndMonth is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fiscalYearEndMonth),
                fiscalYearEndMonth,
                "Fiscal year-end month must be between 1 and 12."
            );
        }

        if (fiscalQuarter is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fiscalQuarter),
                fiscalQuarter,
                "Fiscal quarter must be between 1 and 4."
            );
        }

        // Q4 ends in the fiscal-year-end month of the fiscal year's labelling
        // calendar year; each earlier quarter ends three months before. When
        // that walk crosses January it lands in the previous calendar year.
        var endMonthRaw = fiscalYearEndMonth - 3 * (4 - fiscalQuarter);
        var endMonth = endMonthRaw <= 0 ? endMonthRaw + 12 : endMonthRaw;
        var endYear = endMonthRaw <= 0 ? fiscalYear - 1 : fiscalYear;

        return new DateOnly(endYear, endMonth, DateTime.DaysInMonth(endYear, endMonth));
    }

    /// <summary>
    /// Returns the fiscal period for <paramref name="date"/> using the stock's
    /// detected <see cref="CommonStock.FiscalYearEndMonth"/>, or null when the
    /// fiscal year-end has not been detected yet.
    /// </summary>
    public static FiscalPeriod? GetPeriod(DateOnly date, CommonStock commonStock)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        return commonStock.FiscalYearEndMonth is { } month ? GetPeriod(date, month) : null;
    }

    /// <summary>
    /// The quarter-end date for <paramref name="fiscalQuarter"/>/
    /// <paramref name="fiscalYear"/> using the stock's detected
    /// <see cref="CommonStock.FiscalYearEndMonth"/>, or null when the fiscal
    /// year-end has not been detected yet.
    /// </summary>
    public static DateOnly? GetQuarterEndDate(
        int fiscalYear,
        int fiscalQuarter,
        CommonStock commonStock
    )
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        return commonStock.FiscalYearEndMonth is { } month
            ? GetQuarterEndDate(fiscalYear, fiscalQuarter, month)
            : null;
    }
}
