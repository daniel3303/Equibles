namespace Equibles.Yahoo.Mcp.Captures;

// One row of a GetLatestClosingPrices answer, as printed. A placeholder row (unknown or unreadable symbol, no
// stored bar) has no Listing and carries its Status; a withheld day change or 52-week figure is null, and
// RangeStarred marks a 52-week range the answer starred (a short or split-limited history).
public sealed record LatestClosingPriceCaptureRow(
    string Symbol,
    StockPriceCaptureListing Listing,
    string Status,
    DateOnly? Date,
    decimal? Close,
    decimal? Change,
    decimal? ChangePercent,
    long? Volume,
    decimal? High52Weeks,
    decimal? Low52Weeks,
    bool RangeStarred
);
