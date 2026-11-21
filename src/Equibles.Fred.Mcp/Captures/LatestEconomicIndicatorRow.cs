using Equibles.Fred.Data.Models;

namespace Equibles.Fred.Mcp.Captures;

// One series row of a GetLatestEconomicIndicators answer: its latest and previous stored observations (either may be
// missing) and the change between them the answer printed, null when either value is missing.
public sealed record LatestEconomicIndicatorRow(
    FredSeries Series,
    FredObservation Latest,
    FredObservation Previous,
    decimal? Change
);
