using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.Yahoo.Mcp.Captures;

// What one GetDividendHistory answer showed: the listing and its printed label, whether amounts carry the venue's
// currency, whether record and payment dates were printed, the page of dividends newest first, and the paging note in
// words a reader can act on.
public sealed record DividendHistoryCapture(
    EquityListing Listing,
    string Label,
    bool Venue,
    bool Dated,
    IReadOnlyList<CashDividend> Dividends,
    int Total,
    int Offset,
    IReadOnlyList<string> Notes
);
