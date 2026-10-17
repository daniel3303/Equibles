namespace Equibles.Holdings.Repositories.Models;

/// <summary>One 13F filing a holder identity backfill may read the cover page of.</summary>
public record HolderIdentityBackfillFiling(string AccessionNumber, DateOnly FilingDate);
