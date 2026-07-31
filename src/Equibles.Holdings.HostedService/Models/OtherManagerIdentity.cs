namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// One entry from a 13F's other-manager table, as filed. Carries the identifiers alongside the
/// name so the import can persist a manager the rest of the platform can resolve to an
/// institution, instead of a name string nothing can safely match on.
/// </summary>
/// <remarks>
/// Every field except <see cref="Name"/> is optional at the source: the SEC accepts a manager
/// entry with a name and nothing else. Callers must treat the identifiers as absent rather than
/// falling back to matching on the name.
/// </remarks>
public record OtherManagerIdentity(
    string Name,
    string Cik,
    string Form13FFileNumber,
    string CrdNumber,
    string SecFileNumber
);
