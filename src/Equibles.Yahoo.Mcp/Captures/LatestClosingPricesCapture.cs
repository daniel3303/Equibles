namespace Equibles.Yahoo.Mcp.Captures;

// What one GetLatestClosingPrices answer showed, in table order, plus how many requested tickers went unread
// past the batch cap and the release note the caller was owed.
public sealed record LatestClosingPricesCapture(
    IReadOnlyList<LatestClosingPriceCaptureRow> Rows,
    int UnreadTickers,
    string HoldNote
);
