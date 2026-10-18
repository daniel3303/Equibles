namespace Equibles.Congress.HostedService.Models;

/// <summary>
/// A filing member whose seat and surname point at a BioGuide id that was not assigned, because
/// the member already has another id or because another member row has or derives the same id.
/// </summary>
public sealed record LegislatorIdentityConflict(
    string MemberName,
    string ExistingBioguideId,
    string DerivedBioguideId,
    string Reason
);
