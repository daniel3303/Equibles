using Equibles.Cboe.Data.Models;

namespace Equibles.Cboe.Mcp.Captures;

// What one GetVixHistory answer showed, for McpResultCapture: the daily rows oldest first, the
// requested range, and how many rows that range holds when the answer kept only the newest.
public sealed record VixHistoryCapture(
    IReadOnlyList<CboeVixDaily> Rows,
    DateOnly Start,
    DateOnly End,
    int TotalInRange
);
