using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>
/// One reporting person of a Schedule 13D or 13G, with counts restated onto today's split basis.
/// Persons on one filing each state their own beneficial ownership of the same block, so they are
/// never added together.
/// </summary>
public sealed record BeneficialOwnershipPerson(
    string Name,
    string Cik,
    ReportingPersonTypes Types,
    string CitizenshipOrOrganization,
    long AggregateAmountOwned,
    decimal? PercentOfClass,
    long SoleVotingPower,
    long SharedVotingPower,
    long SoleDispositivePower,
    long SharedDispositivePower,
    bool IsLead
);
