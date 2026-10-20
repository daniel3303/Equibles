using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>A stored 13D/13G detail row without holdings whose CUSIP now resolves to a tracked security.</summary>
public record Schedule13DGRetryCandidate(
    string AccessionNumber,
    FilingType FilingType,
    bool IsAmendment,
    DateOnly FilingDate,
    DateOnly DateOfEvent,
    string FilerCik,
    string IssuerCik,
    string IssuerCusip
);
