using System.Globalization;

namespace Equibles.Finra.Data.Models;

public static class OffExchangeVolumeCoverage
{
    // The ordinal symbol-map fix first re-imported the rolling FINRA window beginning here.
    // Older weeks had already aged out and cannot be repaired from the source.
    public const string CorrectedSymbolResolutionStartWeekIso = "2025-08-11";
    public static readonly DateOnly CorrectedSymbolResolutionStartWeek = DateOnly.ParseExact(
        CorrectedSymbolResolutionStartWeekIso,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture
    );

    public const string HistoricalCaseFoldCaveat =
        "Weeks before "
        + CorrectedSymbolResolutionStartWeekIso
        + " may include volume from a case-variant sibling security because they predate the ordinal FINRA symbol-map fix and can no longer be re-imported from FINRA's rolling source window.";

    public static bool MayIncludeCaseFoldedSiblingVolume(DateOnly weekStartDate) =>
        weekStartDate < CorrectedSymbolResolutionStartWeek;
}
