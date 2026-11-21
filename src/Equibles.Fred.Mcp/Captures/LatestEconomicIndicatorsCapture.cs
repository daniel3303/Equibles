namespace Equibles.Fred.Mcp.Captures;

// What one GetLatestEconomicIndicators answer showed: one country's series in the answer's order (by category, then
// series ID).
public sealed record LatestEconomicIndicatorsCapture(
    string CountryCode,
    IReadOnlyList<LatestEconomicIndicatorRow> Rows
);
