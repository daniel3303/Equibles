namespace Equibles.Holdings.BusinessLogic.Models;

/// <summary>
/// What the deriver needs besides the evidence: today, and the latest date of each Form ADV
/// roster, since an adviser row older than its roster is a former listing, not a current one.
/// </summary>
public record HolderEvidenceContext(
    DateOnly Today,
    DateOnly? LatestRegisteredRoster,
    DateOnly? LatestExemptRoster
);
