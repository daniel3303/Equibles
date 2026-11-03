namespace Equibles.Sec.Data.Helpers;

/// <summary>
/// Whether a company files annual reports with the SEC now, the test every writer of its fiscal
/// year end shares. An annual filer files every year; an older annual report belongs to a past
/// registration, whose year end may have changed since. The window lies inside the document sync
/// floor, so stored Documents and EDGAR's recent filings answer it alike.
/// </summary>
public static class SecAnnualFiler
{
    public const int Years = 2;

    public static bool IsCurrent(DateOnly? latestAnnualReportFiledOn, DateOnly today) =>
        latestAnnualReportFiledOn is { } filed && filed >= today.AddYears(-Years);
}
