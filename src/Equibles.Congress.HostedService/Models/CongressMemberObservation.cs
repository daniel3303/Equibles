using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Models;

public sealed record CongressMemberObservation(
    string FilingName,
    CongressPosition Position,
    string StateDistrict,
    DateOnly ObservedAt
);
