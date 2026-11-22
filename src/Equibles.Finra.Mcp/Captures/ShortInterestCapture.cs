namespace Equibles.Finra.Mcp.Captures;

// What one GetShortInterest answer showed: the listing it named, its settlements oldest first, and how many settlements
// the range holds.
public sealed record ShortInterestCapture(
    string ListedTicker,
    string CompanyName,
    IReadOnlyList<ShortInterestRow> Rows,
    int Total
);
