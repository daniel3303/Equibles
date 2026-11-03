using Equibles.Integrations.Sec.Models;

namespace Equibles.Integrations.Sec;

/// <summary>
/// Registrant facts read from a filer's EDGAR filing index rather than from any document: its
/// business development company election (Form N-54A) and withdrawal (Form N-54C), its newest
/// annual report, the file number EDGAR indexes its periodic reports under, and its newest
/// successor-issuer report.
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

    // The annual reports whose cover states the fiscal year they close.
    private static readonly string[] AnnualReportForms =
    [
        "10-K",
        "10-K/A",
        "10-KT",
        "10-KT/A",
        "20-F",
        "20-F/A",
        "40-F",
        "40-F/A",
    ];

    private static readonly string[] SuccessorForms = ["8-K12B", "8-K12G3", "8-K15D5"];

    /// <summary>
    /// How long after its successor-issuer report a registrant is taken to be inheriting a ticker it
    /// is first seen holding. SEC's directory follows the report within days; an older report belongs
    /// to an earlier reorganization and says nothing about a ticker the registrant takes over now.
    /// </summary>
    public static readonly TimeSpan SuccessorRegistrationWindow = TimeSpan.FromDays(180);

    public static DateOnly? LatestBdcElection(IEnumerable<FilingData> filings) =>
        LatestFiled(filings, "N-54A");

    public static DateOnly? LatestBdcWithdrawal(IEnumerable<FilingData> filings) =>
        LatestFiled(filings, "N-54C");

    public static DateOnly? LatestAnnualReportFiledOn(IEnumerable<FilingData> filings) =>
        filings
            .Where(f => AnnualReportForms.Contains(f.Form) && f.FilingDate != DateOnly.MinValue)
            .Select(f => (DateOnly?)f.FilingDate)
            .Max();

    public static string PeriodicReportFileNumber(IEnumerable<FilingData> filings) =>
        filings
            .Where(f => PeriodicReportForms.Contains(f.Form) && f.FileNumber != null)
            .OrderByDescending(f => f.FilingDate)
            .ThenByDescending(f => f.AccessionNumber, StringComparer.Ordinal)
            .Select(f => f.FileNumber)
            .FirstOrDefault();

    public static SuccessorRegistration LatestSuccessorRegistration(
        IEnumerable<FilingData> filings
    ) =>
        filings
            .Where(f =>
                f.Form != null
                && SuccessorForms.Any(form => f.Form == form || f.Form == form + "/A")
                && f.FilingDate != DateOnly.MinValue
            )
            .OrderByDescending(f => f.FilingDate)
            .ThenByDescending(f => f.AccessionNumber, StringComparer.Ordinal)
            .Select(f => new SuccessorRegistration(f.Form, f.FilingDate, f.AccessionNumber))
            .FirstOrDefault();

    /// <summary>True when the report is recent enough to explain a ticker the registrant now holds.</summary>
    public static bool IsCurrentSuccessorRegistration(
        SuccessorRegistration registration,
        DateOnly today
    ) =>
        registration != null
        && today.DayNumber - registration.FiledOn.DayNumber <= SuccessorRegistrationWindow.Days;

    // An amendment of the form ("N-54A/A") restates the same election, so it counts as one.
    private static DateOnly? LatestFiled(IEnumerable<FilingData> filings, string form) =>
        filings
            .Where(f =>
                (f.Form == form || f.Form == form + "/A") && f.FilingDate != DateOnly.MinValue
            )
            .Select(f => (DateOnly?)f.FilingDate)
            .Max();
}
