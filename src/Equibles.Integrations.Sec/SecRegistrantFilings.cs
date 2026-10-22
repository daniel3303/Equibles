using Equibles.Integrations.Sec.Models;

namespace Equibles.Integrations.Sec;

/// <summary>
/// Registrant facts read from a filer's EDGAR filing index rather than from any document: its
/// business development company election (Form N-54A) and withdrawal (Form N-54C), and the file
/// number EDGAR indexes its periodic reports under.
/// </summary>
public static class SecRegistrantFilings
{
    private static readonly string[] PeriodicReportForms =
    [
        "10-K",
        "10-K/A",
        "10-KT",
        "10-KT/A",
        "10-Q",
        "10-Q/A",
    ];

    public static DateOnly? LatestBdcElection(IEnumerable<FilingData> filings) =>
        LatestFiled(filings, "N-54A");

    public static DateOnly? LatestBdcWithdrawal(IEnumerable<FilingData> filings) =>
        LatestFiled(filings, "N-54C");

    public static string PeriodicReportFileNumber(IEnumerable<FilingData> filings) =>
        filings
            .Where(f => PeriodicReportForms.Contains(f.Form) && f.FileNumber != null)
            .OrderByDescending(f => f.FilingDate)
            .ThenByDescending(f => f.AccessionNumber, StringComparer.Ordinal)
            .Select(f => f.FileNumber)
            .FirstOrDefault();

    // An amendment of the form ("N-54A/A") restates the same election, so it counts as one.
    private static DateOnly? LatestFiled(IEnumerable<FilingData> filings, string form) =>
        filings
            .Where(f =>
                (f.Form == form || f.Form == form + "/A") && f.FilingDate != DateOnly.MinValue
            )
            .Select(f => (DateOnly?)f.FilingDate)
            .Max();
}
