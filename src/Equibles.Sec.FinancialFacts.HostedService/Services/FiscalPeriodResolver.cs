using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.FiscalPeriods;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Derives a fact's (FiscalYear, FiscalPeriod) from the period it actually
/// measures, rather than from the filing's <c>fy</c>/<c>fp</c> identity. SEC
/// ships every value in a filing with the filing's own fiscal-year qualifier
/// (a FY2024 10-K stamps all three comparable years as fy=2024 / fp=FY), so
/// using those fields as period identity collapses distinct actual periods
/// into one row at the unique-index level — see issue #982.
/// </summary>
internal static class FiscalPeriodResolver
{
    /// <summary>
    /// Resolves <paramref name="periodStart"/> / <paramref name="periodEnd"/>
    /// against the company's fiscal-year-end month and day. Returns
    /// <c>null</c> when the FYE is unknown or the period duration doesn't
    /// match any recognised shape — callers fall back to the filing-supplied
    /// identity in that case.
    /// <para>
    /// <paramref name="classifyInterimInstants"/> opts an instant that is NOT at the
    /// fiscal-year end into quarter classification (which fiscal quarter contains the
    /// date). Off by default: callers with an SEC-supplied fp rely on the null
    /// fallback there, and re-labelling their instants would rewrite fiscal
    /// identities corpus-wide. Only fp-less values (6-K interim balance sheets,
    /// which SEC serves with <c>fp = null</c>) opt in — for them the date is the
    /// only identity available.
    /// </para>
    /// </summary>
    public static (int Year, SecFiscalPeriod Period)? Resolve(
        DateOnly periodStart,
        DateOnly periodEnd,
        int? fyeMonth,
        int? fyeDay,
        bool classifyInterimInstants = false
    ) =>
        ReportedFiscalPeriodResolver.Resolve(
            periodStart,
            periodEnd,
            fyeMonth,
            fyeDay,
            classifyInterimInstants
        );
}
