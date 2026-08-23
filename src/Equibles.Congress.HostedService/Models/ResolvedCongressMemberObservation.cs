using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Models;

internal sealed record ResolvedCongressMemberObservation(
    string FilingName,
    string CanonicalName,
    string BioguideId,
    CongressPosition Position,
    string StateDistrict,
    DateOnly ObservedAt
);
