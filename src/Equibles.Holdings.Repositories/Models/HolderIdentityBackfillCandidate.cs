namespace Equibles.Holdings.Repositories.Models;

/// <summary>A holder whose identity numbers predate the cover-page refresh, with its newest 13F.</summary>
public record HolderIdentityBackfillCandidate(
    Guid HolderId,
    string Cik,
    string AccessionNumber,
    DateOnly FilingDate
);
