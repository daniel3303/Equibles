using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>A stored 13D/13G with the type codes of each reporting person.</summary>
public record BeneficialOwnershipEvidenceRow(
    string AccessionNumber,
    FilingType FilingType,
    DateOnly FilingDate,
    string FilerCik,
    ReportingPersonTypes FilingPersonTypes,
    IReadOnlyList<(string Cik, ReportingPersonTypes Types)> Persons
);
