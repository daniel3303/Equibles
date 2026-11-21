using Equibles.Yahoo.Data.Prices;

namespace Equibles.Yahoo.Mcp.Captures;

// What one GetStockPrices answer showed, for McpResultCapture: the bars oldest first (a daily bar is its own
// period), the requested range, how many bars that range holds when the answer kept only the newest, and the
// release note the caller was owed.
public sealed record StockPriceHistoryCapture(
    StockPriceCaptureListing Listing,
    PriceBarInterval Interval,
    DateOnly Start,
    DateOnly End,
    int TotalInRange,
    IReadOnlyList<PriceBar> Bars,
    string HoldNote
);
