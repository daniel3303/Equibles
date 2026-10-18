using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Models;

/// <summary>One stored legislator term with the legislator's surname, as the matcher reads it.</summary>
public sealed record LegislatorTermCandidate(
    string BioguideId,
    string LastName,
    CongressPosition Chamber,
    string State,
    short? District,
    DateOnly StartDate,
    DateOnly EndDate
);
