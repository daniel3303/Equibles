namespace Equibles.Fred.Mcp.Captures;

// What one GetEconomicCalendar answer showed: the requested range, its rows in the answer's order, and the answer's
// truncation and left-out notes in words a reader can act on.
public sealed record EconomicCalendarCapture(
    DateOnly Start,
    DateOnly End,
    IReadOnlyList<EconomicCalendarEntry> Entries,
    IReadOnlyList<string> Notes
);
