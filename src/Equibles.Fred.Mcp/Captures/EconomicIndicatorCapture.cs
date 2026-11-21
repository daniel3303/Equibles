using Equibles.Fred.Data.Models;

namespace Equibles.Fred.Mcp.Captures;

// What one GetEconomicIndicator answer showed, for McpResultCapture: the series, its kept observations oldest first,
// the requested range, how many observations that range holds, and the answer's notes in words a reader can act on.
public sealed record EconomicIndicatorCapture(
    FredSeries Series,
    IReadOnlyList<FredObservation> Observations,
    DateOnly Start,
    DateOnly End,
    int TotalInRange,
    IReadOnlyList<string> Notes
);
