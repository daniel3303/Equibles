using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.Data.Helpers;

/// <summary>
/// The fiscal year a company itself prints, against the one stored. Every stored fiscal year is
/// the calendar year the period ends in, so one key means one period for every company. A
/// company that names its year for the calendar year it starts in (most January year-end
/// retailers: the year ended 2026-02-01 is their fiscal 2025) reads one year lower than the
/// stored number, and <see cref="EquityIssuer.FiscalYearLabelOffset"/> carries that difference.
/// Surfaces print <see cref="For(int, int?)"/> and translate a year a caller typed with
/// <see cref="Stored(int, int?)"/>; keys, joins and stored rows never move.
/// </summary>
public static class FiscalYearLabel
{
    /// <summary>The year to print for a stored fiscal year; the stored year while the naming is unknown.</summary>
    public static int For(int storedFiscalYear, int? labelOffset) =>
        storedFiscalYear + (labelOffset ?? 0);

    /// <summary>The year <paramref name="issuer"/> prints for a stored fiscal year.</summary>
    public static int For(int storedFiscalYear, EquityIssuer issuer) =>
        For(storedFiscalYear, issuer?.FiscalYearLabelOffset);

    /// <summary>
    /// One line for an answer that prints <paramref name="issuer"/>'s fiscal years, or null when
    /// its labels are the stored years. A reader comparing with another source sees why this
    /// company's year reads one lower than its period end.
    /// </summary>
    public static string Note(EquityIssuer issuer) =>
        issuer?.FiscalYearLabelOffset is < 0
            ? "Fiscal years follow the company's own naming: each is named for the calendar year it starts in."
            : null;

    /// <summary>The stored fiscal year a printed (or typed) company year stands for.</summary>
    public static int Stored(int labelledFiscalYear, int? labelOffset) =>
        labelledFiscalYear - (labelOffset ?? 0);

    /// <summary>The stored fiscal year a year typed in <paramref name="issuer"/>'s own naming stands for.</summary>
    public static int Stored(int labelledFiscalYear, EquityIssuer issuer) =>
        Stored(labelledFiscalYear, issuer?.FiscalYearLabelOffset);
}
