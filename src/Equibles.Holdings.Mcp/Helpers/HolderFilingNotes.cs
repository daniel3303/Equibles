using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Mcp.Helpers;

namespace Equibles.Holdings.Mcp.Helpers;

/// <summary>
/// The lines a single-filer 13F answer owes its reader about the filing behind the quarter shown:
/// when it was filed and loaded, and why a requested quarter is not the one shown.
/// </summary>
public static class HolderFilingNotes
{
    // Filings named before the line falls back to a count.
    private const int MaxFilingsListed = 3;

    /// <summary>
    /// The note for a requested quarter end this filer has no 13F for, or null when the request is
    /// not a calendar quarter end (the shared snap note already fits a mid-quarter date).
    /// </summary>
    public static string MissingQuarter(
        string holderName,
        DateOnly requested,
        DateOnly shown,
        IReadOnlyList<DateOnly> reportDates
    )
    {
        if (
            !HoldingsCorpusCoverage.IsCalendarQuarterEnd(requested)
            || reportDates.Contains(requested)
        )
            return null;

        // "On file here", never "not filed": the filer may have filed late, filed a notice, or
        // withheld the quarter, and none of those is knowable from the absence alone.
        return requested > reportDates[0]
            ? $"Note: {holderName} has no 13F for {Date(requested)} on file here — showing its latest report, {Date(shown)}. A quarter appears once the filer's 13F is filed with the SEC and loaded here; GetLatest13FFilings lists the newest loaded filings."
            : $"Note: {holderName} has no 13F for {Date(requested)} on file here — showing the nearest report before it, {Date(shown)}.";
    }

    /// <summary>
    /// One line naming the 13F filings behind a filer's quarter, newest first, or null when the
    /// quarter has no filing rollup.
    /// </summary>
    public static string Filings(DateOnly reportDate, IEnumerable<InstitutionalFiling> filings)
    {
        var ordered = filings
            .Where(filing => filing.FilingType == FilingType.Form13F)
            .OrderByDescending(filing => filing.FilingDate)
            .ThenByDescending(filing => filing.AccessionNumber, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count == 0)
            return null;

        var listed = ordered.Take(MaxFilingsListed).Select(Describe);
        var more =
            ordered.Count > MaxFilingsListed
                ? $"; {ordered.Count - MaxFilingsListed} earlier filing(s) not listed"
                : "";
        return $"13F filing for {Date(reportDate)}: {string.Join("; ", listed)}{more}.";
    }

    // The rollup row's creation time: a replay keeps it, a filing removed and imported again moves it.
    private static string Describe(InstitutionalFiling filing) =>
        $"{(filing.IsAmendment ? "13F-HR/A" : "13F-HR")} filed {Date(filing.FilingDate)}, loaded {LoadedAt(filing.CreationTime)} (accession {filing.AccessionNumber})";

    public static string LoadedAt(DateTime utc) =>
        McpFormat.Invariant(utc, "yyyy-MM-dd HH:mm") + " UTC";

    private static string Date(DateOnly date) => McpFormat.Invariant(date, "yyyy-MM-dd");
}
