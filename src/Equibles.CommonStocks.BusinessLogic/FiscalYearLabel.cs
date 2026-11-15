namespace Equibles.CommonStocks.BusinessLogic;

/// <summary>
/// The fiscal year a company itself prints, against the one stored. Every stored fiscal year is
/// the calendar year the period ends in, so one key means one period for every company. A
/// company that names its year for the calendar year it starts in (most January year-end
/// retailers: the year ended 2026-02-01 is their fiscal 2025) reads one year lower than the
/// stored number, and <see cref="Equibles.CommonStocks.Data.Models.EquityIssuer.FiscalYearLabelOffset"/>
/// carries that difference. Surfaces print <see cref="For"/> and translate a year a caller typed
/// with <see cref="Stored"/>; keys, joins and stored rows never move.
/// </summary>
public static class FiscalYearLabel
{
    /// <summary>The year to print for a stored fiscal year; the stored year while the naming is unknown.</summary>
    public static int For(int storedFiscalYear, int? labelOffset) =>
        storedFiscalYear + (labelOffset ?? 0);

    /// <summary>The stored fiscal year a printed (or typed) company year stands for.</summary>
    public static int Stored(int labelledFiscalYear, int? labelOffset) =>
        labelledFiscalYear - (labelOffset ?? 0);
}
