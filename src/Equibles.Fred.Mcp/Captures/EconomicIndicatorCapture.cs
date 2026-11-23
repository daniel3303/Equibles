using Equibles.Fred.Data.Models;

namespace Equibles.Fred.Mcp.Captures;

// What one GetEconomicIndicator answer showed, for McpResultCapture: the series, its kept observations oldest first,
// the requested range, how many observations that range holds, and the answer's notes in words a reader can act on.
// YearAgoChanges, when the answer showed the change from a year earlier, holds it per observation in the same order
// (null where stored data has no base), in percentage points when ChangeInPoints, else in percent.
public sealed record EconomicIndicatorCapture(
    FredSeries Series,
    IReadOnlyList<FredObservation> Observations,
    DateOnly Start,
    DateOnly End,
    int TotalInRange,
    IReadOnlyList<string> Notes,
    IReadOnlyList<decimal?> YearAgoChanges = null,
    bool ChangeInPoints = false
);
