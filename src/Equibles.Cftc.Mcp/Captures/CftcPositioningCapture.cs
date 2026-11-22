namespace Equibles.Cftc.Mcp.Captures;

// What one GetCftcPositioning answer showed: the contract and its category as titled, its reports oldest first, and how
// many reports the range holds.
public sealed record CftcPositioningCapture(
    string MarketName,
    string MarketCode,
    string Category,
    IReadOnlyList<CftcReportRow> Reports,
    int Total
);
