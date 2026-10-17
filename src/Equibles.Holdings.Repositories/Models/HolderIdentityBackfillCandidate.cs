namespace Equibles.Holdings.Repositories.Models;

/// <summary>
/// A holder whose identity numbers predate the cover-page refresh: its stored numbers and its
/// newest 13F filings, newest first.
/// </summary>
public record HolderIdentityBackfillCandidate(
    Guid HolderId,
    string Cik,
    string Form13FFileNumber,
    string CrdNumber,
    string SecFileNumber,
    IReadOnlyList<HolderIdentityBackfillFiling> Filings
);
