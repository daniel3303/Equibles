using Equibles.Fred.Data.Models;

namespace Equibles.Fred.Mcp.Captures;

// One row of a GetEconomicCalendar answer: a release date with its market, release, reference period, importance
// tier and the series the release updates.
public sealed record EconomicCalendarEntry(
    DateOnly Date,
    string CountryCode,
    string ReleaseName,
    string ReferencePeriod,
    FredReleaseImportance Importance,
    IReadOnlyList<string> Series
);
