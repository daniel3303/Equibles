namespace Equibles.Holdings.Mcp.Captures;

/// <summary>
/// How many of the compared filers added shares and how many cut, with the new and sold-out positions among them.
/// </summary>
public sealed record InstitutionalActivityCounts(
    int Adding,
    int NewPositions,
    int Cutting,
    int SoldOut
);
