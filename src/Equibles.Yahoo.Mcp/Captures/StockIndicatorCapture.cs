using Equibles.Yahoo.Data.Prices;

namespace Equibles.Yahoo.Mcp.Captures;

// What one technical-indicator answer showed, oldest first: the daily bars of the rows printed and, per line
// (Lines[i]), the value printed on each bar (Values[i][bar]; null where the table prints a dash). Parameters
// is the window the title states, and TotalInRange counts the rows the range held before maxResults cut it.
public sealed record StockIndicatorCapture(
    StockPriceCaptureListing Listing,
    StockIndicatorKind Kind,
    string Parameters,
    IReadOnlyList<string> Lines,
    IReadOnlyList<PriceBar> Bars,
    IReadOnlyList<IReadOnlyList<decimal?>> Values,
    int TotalInRange,
    string HoldNote
);
