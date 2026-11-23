namespace Equibles.Holdings.Mcp.Captures;

/// <summary>
/// One ranked mover. <see cref="Remark"/> is what the answer prints after the name (a first 13F, or the filers
/// this one continues), null for most; a new position has no previous shares and a sold-out one no current shares.
/// </summary>
public sealed record InstitutionalActivityCaptureRow(
    Guid HolderId,
    string Name,
    string Remark,
    string Cik,
    long PreviousShares,
    long CurrentShares,
    long DeltaShares,
    long DeltaValue
);
