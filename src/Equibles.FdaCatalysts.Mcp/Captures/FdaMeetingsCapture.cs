namespace Equibles.FdaCatalysts.Mcp.Captures;

// What one GetFdaAdvisoryCommitteeMeetings answer showed: the searched window, its meetings soonest first, and how many
// meetings the window holds.
public sealed record FdaMeetingsCapture(
    DateOnly Start,
    DateOnly End,
    IReadOnlyList<FdaMeetingRow> Meetings,
    int Total
);
