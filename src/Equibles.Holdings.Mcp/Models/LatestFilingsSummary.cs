namespace Equibles.Holdings.Mcp.Models;

/// <summary>Filings loaded per recent SEC filing day, newest first, and the newest load among them.</summary>
public sealed record LatestFilingsSummary(
    IReadOnlyList<(DateOnly Day, int Count)> FilingDays,
    DateTime? NewestLoad
);
